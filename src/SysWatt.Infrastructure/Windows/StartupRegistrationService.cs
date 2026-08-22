using Microsoft.Win32;

namespace SysWatt.Infrastructure.Windows;

public interface IStartupRegistrationService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SysWatt";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, false);
    }
}
