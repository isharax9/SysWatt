using System.Diagnostics;
using Microsoft.Win32;

namespace SysWatt.Infrastructure.Windows;

public interface IStartupRegistrationService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

/// <summary>
/// Registers SysWatt as a Windows startup application using Task Scheduler so that
/// it launches with highest privileges (administrator) at user logon.
/// Falls back gracefully and also cleans up any legacy registry Run-key entry.
/// </summary>
public sealed class StartupRegistrationService : IStartupRegistrationService
{
    // Legacy registry key — kept only for cleanup of old installations.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "SysWatt";

    // Task Scheduler task name (appears in Task Scheduler under the root folder).
    private const string TaskName = "SysWatt";

    public bool IsEnabled()
    {
        // Check Task Scheduler first (new style).
        if (TaskExists()) return true;

        // Fall back: check legacy registry key (old installations).
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(RegistryValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        // Always remove any legacy registry Run-key entry from older versions.
        RemoveLegacyRegistryEntry();

        if (enabled)
            CreateScheduledTask();
        else
            DeleteScheduledTask();
    }

    // -------------------------------------------------------------------------
    // Task Scheduler helpers (via schtasks.exe — built into every Windows install)
    // -------------------------------------------------------------------------

    private static bool TaskExists()
    {
        try
        {
            var result = RunSchtasks($"/Query /TN \"{TaskName}\" /FO LIST");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CreateScheduledTask()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Executable path is unavailable.");

        // Build a minimal Task Scheduler XML that:
        //   - Triggers at current-user logon
        //   - Runs with highest privileges (elevated)
        //   - Passes --minimized so the dashboard stays hidden on auto-start
        var userId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts SysWatt at user logon with administrator access.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{userId}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{userId}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{executable}"</Command>
                  <Arguments>--minimized</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        // Write XML to a temp file so we can pass it to schtasks /XML.
        var tempXml = Path.Combine(Path.GetTempPath(), "SysWatt_task.xml");
        File.WriteAllText(tempXml, xml, System.Text.Encoding.Unicode);
        try
        {
            // /F overwrites any existing task with the same name silently.
            var result = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tempXml}\" /F");
            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"schtasks /Create failed (exit {result.ExitCode}).\n{result.StdErr}");
        }
        finally
        {
            try { File.Delete(tempXml); } catch { /* best-effort */ }
        }
    }

    private static void DeleteScheduledTask()
    {
        if (!TaskExists()) return;
        var result = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"schtasks /Delete failed (exit {result.ExitCode}).\n{result.StdErr}");
    }

    private static void RemoveLegacyRegistryEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
        }
        catch { /* best-effort cleanup */ }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunSchtasks(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
