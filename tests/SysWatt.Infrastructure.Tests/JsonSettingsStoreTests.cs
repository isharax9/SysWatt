using Microsoft.Extensions.Logging.Abstractions;
using SysWatt.Core.Power;
using SysWatt.Core.Settings;
using SysWatt.Infrastructure.Settings;

namespace SysWatt.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SysWatt.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoundTripsAndSanitizesSettings()
    {
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, _directory);
        await store.SaveAsync(new AppSettings { PollingIntervalMilliseconds = 1, Power = new PowerModelSettings(55, .9) }, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(500, loaded.PollingIntervalMilliseconds);
        Assert.Equal(55, loaded.Power.BaseSystemWatts);
    }

    [Fact]
    public async Task MalformedSettingsArePreservedAndDefaultsReturned()
    {
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, _directory);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(store.SettingsPath, "{nope", TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, loaded.SchemaVersion);
        Assert.Single(Directory.GetFiles(_directory, "*.invalid-*"));
    }

    [Fact]
    public async Task VersionOnePowerSettingsMigrateWithoutDoubleCountingNewContributors()
    {
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, _directory);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(store.SettingsPath,
            """
            {
              "SchemaVersion": 1,
              "Power": { "BaseSystemWatts": 45, "PsuEfficiency": 0.9 }
            }
            """, TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, loaded.SchemaVersion);
        Assert.Equal(45, loaded.Power.PcAuxiliaryWatts);
        Assert.Equal(0, loaded.Power.ExternalAcWatts);
    }

    [Fact]
    public async Task VersionThreeDefaultCpuFloorMigratesButCustomCalibrationIsPreserved()
    {
        var defaultDirectory = Path.Combine(_directory, "default");
        var customDirectory = Path.Combine(_directory, "custom");
        var oldJson = """
            { "SchemaVersion": 3, "Power": { "CpuIdleWatts": OLD_VALUE } }
            """;
        var defaultStore = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, defaultDirectory);
        var customStore = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, customDirectory);
        Directory.CreateDirectory(defaultDirectory);
        Directory.CreateDirectory(customDirectory);
        await File.WriteAllTextAsync(defaultStore.SettingsPath, oldJson.Replace("OLD_VALUE", "8"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(customStore.SettingsPath, oldJson.Replace("OLD_VALUE", "27"), TestContext.Current.CancellationToken);

        var migratedDefault = await defaultStore.LoadAsync(TestContext.Current.CancellationToken);
        var migratedCustom = await customStore.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, migratedDefault.SchemaVersion);
        Assert.Equal(22, migratedDefault.Power.CpuIdleWatts);
        Assert.Equal(27, migratedCustom.Power.CpuIdleWatts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
