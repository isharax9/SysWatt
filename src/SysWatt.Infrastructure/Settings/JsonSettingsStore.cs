using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SysWatt.Core.Settings;

namespace SysWatt.Infrastructure.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsPath { get; }

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger) : this(logger, null) { }

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger, string? rootOverride)
    {
        _logger = logger;
        var executableDirectory = AppContext.BaseDirectory;
        var portable = File.Exists(Path.Combine(executableDirectory, "portable.flag"));
        var root = rootOverride ?? (portable
            ? Path.Combine(executableDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysWatt"));
        SettingsPath = Path.Combine(root, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _json, cancellationToken).ConfigureAwait(false);
            return Migrate(settings ?? new AppSettings()).Sanitize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = $"{SettingsPath}.invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(SettingsPath, backup); }
            catch (Exception backupError) { _logger.LogWarning(backupError, "Could not preserve invalid settings file."); }
            _logger.LogWarning(ex, "Settings were invalid and defaults will be used. Invalid copy: {Backup}", backup);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings = settings.Sanitize();
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = SettingsPath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _json, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, SettingsPath, true);
    }

    private static AppSettings Migrate(AppSettings settings) => settings.SchemaVersion switch
    {
        1 => settings with
        {
            SchemaVersion = 2,
            Power = settings.Power with
            {
                StorageWatts = 0,
                FanCount = 0,
                WattsPerFan = 0,
                OtherCoolingWatts = 0,
                UsbPeripheralWatts = 0,
                DisplayWatts = 0,
                ExternalPeripheralWatts = 0,
                OtherWallWatts = 0
            }
        },
        2 => settings,
        _ => settings with { SchemaVersion = 2 }
    };
}
