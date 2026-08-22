using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysWatt.App.ViewModels;
using SysWatt.App.Views;
using SysWatt.App.Windows;
using SysWatt.Core.Alerts;
using SysWatt.Core.History;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Power;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;
using SysWatt.Infrastructure.Diagnostics;
using SysWatt.Infrastructure.Hardware;
using SysWatt.Infrastructure.Monitoring;
using SysWatt.Infrastructure.Settings;
using SysWatt.Infrastructure.Windows;

namespace SysWatt.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconService? _tray;
    private DashboardWindow? _dashboard;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings = new();
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimary)
        {
            await _singleInstance.SignalPrimaryAsync();
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        try
        {
            var settingsStore = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance);
            _settings = await settingsStore.LoadAsync();

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Services.AddSingleton<ISettingsStore>(settingsStore);
            builder.Services.AddSingleton(_settings);
            builder.Services.AddSingleton<ISensorNormalizer, SensorNormalizer>();
            builder.Services.AddSingleton<IPowerEstimationService, PowerEstimationService>();
            builder.Services.AddSingleton<IAlertEvaluator, AlertEvaluator>();
            builder.Services.AddSingleton<ISessionHistory>(_ => new SessionHistory(300));
            builder.Services.AddSingleton<IRawSensorProvider, LibreHardwareMonitorProvider>();
            builder.Services.AddSingleton<IRawSensorProvider, WindowsMemoryProvider>();
            builder.Services.AddSingleton<IMonitoringService, MonitoringService>();
            builder.Services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
            builder.Services.AddSingleton<IDiagnosticExporter, DiagnosticExporter>();
            builder.Services.AddSingleton<DashboardViewModel>();
            _host = builder.Build();
            await _host.StartAsync();

            var monitoring = _host.Services.GetRequiredService<IMonitoringService>();
            _dashboard = new DashboardWindow { DataContext = _host.Services.GetRequiredService<DashboardViewModel>() };
            _dashboard.SettingsRequested += (_, _) => OpenSettings();
            _tray = new TrayIconService(monitoring, _settings);
            _tray.OpenRequested += (_, _) => _dashboard.ToggleNearTray();
            _tray.SettingsRequested += (_, _) => OpenSettings();
            _tray.ExitRequested += async (_, _) => await ExitAsync();
            _tray.StartupChanged += async (_, enabled) => await SetStartupAsync(enabled);
            _singleInstance.StartListening(() => Dispatcher.BeginInvoke(_dashboard.ShowNearTray));
            await monitoring.StartAsync();

            if (e.Args.Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(2500);
                await ExitAsync();
                return;
            }

            var commandLineMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            if (!_settings.StartMinimized && !commandLineMinimized) _dashboard.ShowNearTray();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"SysWatt could not start.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private void OpenSettings()
    {
        if (_host is null || _dashboard is null) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        var viewModel = new SettingsViewModel(_settings,
            _host.Services.GetRequiredService<ISettingsStore>(),
            _host.Services.GetRequiredService<IStartupRegistrationService>(),
            _host.Services.GetRequiredService<IMonitoringService>());
        viewModel.Saved += (_, settings) => { _settings = settings; _tray?.ApplySettings(settings); };
        _settingsWindow = new SettingsWindow(viewModel) { Owner = _dashboard };
        _settingsWindow.ExportDiagnosticsRequested += async (_, _) => await ExportDiagnosticsAsync();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (_host is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export SysWatt diagnostics",
            Filter = "JSON report (*.json)|*.json",
            FileName = $"syswatt-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(_settingsWindow) != true) return;
        var monitoring = _host.Services.GetRequiredService<IMonitoringService>();
        try
        {
            await _host.Services.GetRequiredService<IDiagnosticExporter>()
                .ExportAsync(dialog.FileName, monitoring.LastRawReadings, monitoring.Current);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Diagnostics could not be exported.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SetStartupAsync(bool enabled)
    {
        if (_host is null || _settings.StartWithWindows == enabled) return;
        try
        {
            _host.Services.GetRequiredService<IStartupRegistrationService>().SetEnabled(enabled);
            _settings = _settings with { StartWithWindows = enabled };
            await _host.Services.GetRequiredService<ISettingsStore>().SaveAsync(_settings);
            _host.Services.GetRequiredService<IMonitoringService>().ApplySettings(_settings);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Startup registration could not be changed.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning);
            _tray?.ApplySettings(_settings);
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            _settingsWindow?.Close();
            _dashboard?.CloseForExit();
            _tray?.Dispose();
            _tray = null;
            if (_host is not null)
            {
                var host = _host;
                _host = null;
                try { await host.Services.GetRequiredService<IMonitoringService>().StopAsync(); }
                catch (OperationCanceledException) { }
                await host.StopAsync();
                if (host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync();
                else host.Dispose();
            }
        }
        finally
        {
            _singleInstance?.Dispose();
            _singleInstance = null;
            Shutdown();
        }
    }
}
