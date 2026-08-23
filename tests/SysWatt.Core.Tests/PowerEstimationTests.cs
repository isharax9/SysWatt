using SysWatt.Core.Power;

namespace SysWatt.Core.Tests;

public sealed class PowerEstimationTests
{
    private readonly PowerEstimationService _service = new();

    [Fact]
    public void CompleteInputs_AddEachComponentExactlyOnce()
    {
        var result = _service.Calculate(65, 170, new PowerModelSettings(45, 0.8, StorageWatts: 0, FanCount: 0, UsbPeripheralWatts: 0));
        Assert.Equal(280, result.EstimatedDcWatts);
        Assert.Equal(350, result.EstimatedWallWatts);
        Assert.False(result.IsPartial);
    }

    [Fact]
    public void MissingGpu_IsMarkedPartialAndUsesConfiguredIdleModel()
    {
        var result = _service.Calculate(65, null, new PowerModelSettings(45, 0.9, StorageWatts: 0, FanCount: 0, UsbPeripheralWatts: 0));
        Assert.Equal(117, result.EstimatedDcWatts);
        Assert.True(result.IsPartial);
        Assert.True(result.GpuIsModeled);
    }

    [Theory]
    [InlineData(0.49)]
    [InlineData(1.01)]
    public void InvalidEfficiencyIsRejected(double efficiency) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Calculate(1, 1, new PowerModelSettings(1, efficiency)));

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void EfficiencyBoundariesAreAccepted(double efficiency) =>
        Assert.True(_service.Calculate(1, 1, new PowerModelSettings(1, efficiency)).EstimatedWallWatts > 0);

    [Fact]
    public void AddsPcAuxiliariesBeforePsuLossAndExternalLoadsAfterPsuLoss()
    {
        var settings = new PowerModelSettings(
            BaseSystemWatts: 30,
            PsuEfficiency: .8,
            StorageWatts: 8,
            FanCount: 4,
            WattsPerFan: 2,
            OtherCoolingWatts: 5,
            UsbPeripheralWatts: 4,
            DisplayWatts: 60,
            ExternalPeripheralWatts: 10,
            OtherWallWatts: 2);

        var result = _service.Calculate(50, 100, settings);

        Assert.Equal(205, result.EstimatedDcWatts);
        Assert.Equal(328.2, result.EstimatedWallWatts);
        Assert.Contains("4 fans = 8 W", result.Formula);
    }

    [Fact]
    public void MissingComponentSensors_AreModeledFromUtilization()
    {
        var settings = new PowerModelSettings(BaseSystemWatts: 0, PsuEfficiency: 1, StorageWatts: 0,
            FanCount: 0, UsbPeripheralWatts: 0, CpuIdleWatts: 10, CpuPeakWatts: 110,
            GpuIdleWatts: 5, GpuPeakWatts: 205);

        var result = _service.Calculate(null, null, settings, cpuUsage: 100, gpuUsage: 0);

        Assert.Equal(110, result.EffectiveCpuWatts);
        Assert.Equal(5, result.EffectiveGpuWatts);
        Assert.True(result.CpuIsModeled);
        Assert.True(result.GpuIsModeled);
        Assert.Equal(115, result.EstimatedWallWatts);
    }

    [Fact]
    public void StoragePower_FollowsActivityAndThroughput()
    {
        var settings = new PowerModelSettings(BaseSystemWatts: 0, PsuEfficiency: 1, StorageWatts: 12,
            FanCount: 0, UsbPeripheralWatts: 0, StorageDeviceCount: 2, StorageIdleWattsPerDevice: 1,
            StorageThroughputCeilingMBps: 500);

        var idle = _service.Calculate(0, 0, settings, storageActivity: 0, storageReadMBps: 0, storageWriteMBps: 0);
        var active = _service.Calculate(0, 0, settings, storageActivity: 100, storageReadMBps: 0, storageWriteMBps: 0);

        Assert.Equal(2, idle.EffectiveStorageWatts);
        Assert.Equal(12, active.EffectiveStorageWatts);
    }

    [Fact]
    public void DesktopDefaultModel_DoesNotUseAnUnrealisticSingleDigitIdleFloor()
    {
        var settings = new PowerModelSettings(BaseSystemWatts: 0, PsuEfficiency: 1, StorageWatts: 0,
            FanCount: 0, UsbPeripheralWatts: 0);

        var result = _service.Calculate(null, 0, settings, cpuUsage: 5);

        Assert.InRange(result.EffectiveCpuWatts, 23, 25);
        Assert.True(result.CpuIsModeled);
    }
}
