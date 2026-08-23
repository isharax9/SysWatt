using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysWatt.App.ViewModels;
using SysWatt.App.Views;
using SysWatt.App.Windows;
using SysWatt.App.Theming;
using SysWatt.Core.Alerts;
using SysWatt.Core.History;
using SysWatt.Core.Energy;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Power;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;
using SysWatt.Infrastructure.Diagnostics;
using SysWatt.Infrastructure.Energy;
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
    private TrayDashboardWindow? _trayDashboard;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private AppSettings _settings = new();
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var isSettingsPreview = e.Args.Any(a => a.Equals("--preview-settings", StringComparison.OrdinalIgnoreCase));
        var isDashboardPreview = e.Args.Any(a => a.Equals("--preview-dashboard", StringComparison.OrdinalIgnoreCase));
        var isTrayPreview = e.Args.Any(a => a.Equals("--preview-tray", StringComparison.OrdinalIgnoreCase));
        var isLightPreview = e.Args.Any(a => a.Equals("--preview-light", StringComparison.OrdinalIgnoreCase));
        var isSmokeTest = e.Args.Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        var sensorDiagnosticArgument = Array.FindIndex(e.Args, a => a.Equals("--diagnose-sensors", StringComparison.OrdinalIgnoreCase));
        var isSensorDiagnostic = sensorDiagnosticArgument >= 0;
        if (isSensorDiagnostic) ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var instanceDiscriminator = isSettingsPreview ? "SettingsPreview"
            : isDashboardPreview ? "DashboardPreview"
            : isTrayPreview ? "TrayPreview"
            : isSmokeTest ? "SmokeTest"
            : isSensorDiagnostic ? "SensorDiagnostic"
            : null;
        _singleInstance = new SingleInstanceCoordinator(instanceDiscriminator);
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
            if (isLightPreview) _settings = _settings with { Theme = AppTheme.Light };
            if (isTrayPreview) _settings = _settings with { TrayDashboardPinned = true };
            ThemeManager.Apply(_settings.Theme);

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Services.AddSingleton<ISettingsStore>(settingsStore);
            builder.Services.AddSingleton(_settings);
            builder.Services.AddSingleton<ISensorNormalizer, SensorNormalizer>();
            builder.Services.AddSingleton<IPowerEstimationService, PowerEstimationService>();
            builder.Services.AddSingleton<IHardwareInventoryService, WindowsHardwareInventoryService>();
            builder.Services.AddSingleton<IAlertEvaluator, AlertEvaluator>();
            builder.Services.AddSingleton<ISessionHistory>(_ => new SessionHistory(14_400));
            builder.Services.AddSingleton<IEnergyHistoryStore, SqliteEnergyHistoryStore>();
            builder.Services.AddSingleton<IRawSensorProvider, HWiNFOSharedMemoryProvider>();
            builder.Services.AddSingleton<IRawSensorProvider, LibreHardwareMonitorProvider>();
            builder.Services.AddSingleton<IRawSensorProvider, WindowsPerformanceProvider>();
            builder.Services.AddSingleton<IRawSensorProvider, WindowsMemoryProvider>();
            builder.Services.AddSingleton<IMonitoringService, MonitoringService>();
            builder.Services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
            builder.Services.AddSingleton<IDiagnosticExporter, DiagnosticExporter>();
            builder.Services.AddSingleton<DashboardViewModel>();
            _host = builder.Build();
            await _host.StartAsync();

            var monitoring = _host.Services.GetRequiredService<IMonitoringService>();
            if (isSensorDiagnostic)
            {
                var outputPath = sensorDiagnosticArgument + 1 < e.Args.Length && !e.Args[sensorDiagnosticArgument + 1].StartsWith("--", StringComparison.Ordinal)
                    ? Path.GetFullPath(e.Args[sensorDiagnosticArgument + 1])
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysWatt", $"sensor-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var diagnosticStartedAt = DateTimeOffset.UtcNow;
                await monitoring.StartAsync();
                var timeout = DateTimeOffset.UtcNow.AddSeconds(30);
                while ((monitoring.LastRawReadings.Count == 0 || monitoring.Current.Timestamp <= diagnosticStartedAt) && DateTimeOffset.UtcNow < timeout)
                    await Task.Delay(200);
                await _host.Services.GetRequiredService<IDiagnosticExporter>()
                    .ExportAsync(outputPath, monitoring.LastRawReadings, monitoring.Current);
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }

            var dashboardViewModel = _host.Services.GetRequiredService<DashboardViewModel>();
            dashboardViewModel.SettingsChanged += (_, settings) => { _settings = settings; ThemeManager.Apply(settings.Theme); _tray?.ApplySettings(settings); };
            _dashboard = new DashboardWindow { DataContext = dashboardViewModel };
            _trayDashboard = new TrayDashboardWindow { DataContext = dashboardViewModel };
            _dashboard.SettingsRequested += (_, _) => OpenSettings();
            _dashboard.AboutRequested += (_, _) => OpenAbout();
            _dashboard.RestartElevatedRequested += async (_, _) => await RestartElevatedAsync();
            _trayDashboard.OpenFullDashboardRequested += (_, _) => _dashboard.ShowDashboard();
            _tray = new TrayIconService(monitoring, _settings);
            _tray.QuickDashboardRequested += (_, _) => _trayDashboard.ToggleNearTray();
            _tray.MainDashboardRequested += (_, _) => _dashboard.ShowDashboard();
            _tray.SettingsRequested += (_, _) => OpenSettings();
            _tray.ExitRequested += async (_, _) => await ExitAsync();
            _tray.StartupChanged += async (_, enabled) => await SetStartupAsync(enabled);
            _singleInstance.StartListening(() => Dispatcher.BeginInvoke(_dashboard.ShowDashboard));
            await monitoring.StartAsync();

            if (isSmokeTest)
            {
                var smokeSettingsViewModel = new SettingsViewModel(_settings,
                    _host.Services.GetRequiredService<ISettingsStore>(),
                    _host.Services.GetRequiredService<IStartupRegistrationService>(),
                    monitoring);
                var smokeSettingsWindow = new SettingsWindow(smokeSettingsViewModel);
                smokeSettingsWindow.Close();
                var smokeAboutWindow = new AboutWindow();
                smokeAboutWindow.Close();
                await Task.Delay(2500);
                await ExitAsync();
                return;
            }

            if (e.Args.Any(a => a.Equals("--preview-settings", StringComparison.OrdinalIgnoreCase)))
            {
                OpenSettings();
                await Task.Delay(15000);
                await ExitAsync();
                return;
            }

            var commandLineMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            if (isDashboardPreview)
            {
                _dashboard.ShowDashboard();
                await Task.Delay(15000);
                await ExitAsync();
                return;
            }
            if (isTrayPreview)
            {
                _trayDashboard.ToggleNearTray();
                await Task.Delay(15000);
                await ExitAsync();
                return;
            }

            if (!_settings.StartMinimized && !commandLineMinimized) _dashboard.ShowDashboard();
        }
        catch (Exception ex)
        {
            var details = string.Join("\n→ ", EnumerateExceptionMessages(ex));
            System.Windows.MessageBox.Show($"SysWatt could not start.\n\n{details}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private static IEnumerable<string> EnumerateExceptionMessages(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!) yield return current.Message;
    }

    private void OpenSettings()
    {
        if (_host is null || _dashboard is null) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        var viewModel = new SettingsViewModel(_settings,
            _host.Services.GetRequiredService<ISettingsStore>(),
            _host.Services.GetRequiredService<IStartupRegistrationService>(),
            _host.Services.GetRequiredService<IMonitoringService>());
        viewModel.Saved += (_, settings) =>
        {
            _settings = settings;
            ThemeManager.Apply(settings.Theme);
            _tray?.ApplySettings(settings);
            _host.Services.GetRequiredService<DashboardViewModel>().ApplySettings(settings);
        };
        _settingsWindow = new SettingsWindow(viewModel);
        if (_dashboard.IsVisible) _settingsWindow.Owner = _dashboard;
        else _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _settingsWindow.ExportDiagnosticsRequested += async (_, _) => await ExportDiagnosticsAsync();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OpenAbout()
    {
        if (_dashboard is null) return;
        if (_aboutWindow is { IsVisible: true }) { _aboutWindow.Activate(); return; }
        _aboutWindow = new AboutWindow { Owner = _dashboard };
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
    }

    private async Task RestartElevatedAsync()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = executable, UseShellExecute = true, Verb = "runas" });
            await ExitAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"SysWatt could not restart with administrator access.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            _aboutWindow?.Close();
            _trayDashboard?.CloseForExit();
            _dashboard?.CloseForExit();
            _tray?.Dispose();
            _tray = null;
            if (_host is not null)
            {
                var host = _host;
                _host = null;
                try
                {
                    await host.Services.GetRequiredService<IMonitoringService>()
                        .StopAsync()
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { }

                using (var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    try { await host.StopAsync(stopTimeout.Token); }
                    catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested) { }
                }

                try
                {
                    await Task.Run(async () =>
                    {
                        if (host is IAsyncDisposable asyncHost)
                            await asyncHost.DisposeAsync();
                        else
                            host.Dispose();
                    }).WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (TimeoutException) { }
            }
        }
        finally
        {
            _singleInstance?.Dispose();
            _singleInstance = null;
            Shutdown();
            Environment.Exit(0);
        }
    }
}
