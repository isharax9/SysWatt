using SysWatt.Core.Power;

namespace SysWatt.Core.Tests;

public sealed class PowerEstimationTests
{
    private readonly PowerEstimationService _service = new();

    [Fact]
    public void CompleteInputs_AddEachComponentExactlyOnce()
    {
        var result = _service.Calculate(65, 170, new PowerModelSettings(45, 0.8));
        Assert.Equal(280, result.EstimatedDcWatts);
        Assert.Equal(350, result.EstimatedWallWatts);
        Assert.False(result.IsPartial);
    }

    [Fact]
    public void MissingGpu_IsMarkedPartialAndDoesNotInventAReading()
    {
        var result = _service.Calculate(65, null, new PowerModelSettings(45, 0.9));
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
}
