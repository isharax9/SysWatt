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
    public void MissingGpu_IsMarkedPartialAndDoesNotInventAReading()
    {
        var result = _service.Calculate(65, null, new PowerModelSettings(45, 0.9, StorageWatts: 0, FanCount: 0, UsbPeripheralWatts: 0));
        Assert.Equal(110, result.EstimatedDcWatts);
        Assert.True(result.IsPartial);
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
}
