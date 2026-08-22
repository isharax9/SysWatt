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
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Single(Directory.GetFiles(_directory, "*.invalid-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
