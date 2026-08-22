using SysWatt.Core.Alerts;
using SysWatt.Core.Sensors;

namespace SysWatt.Core.Tests;

public sealed class AlertEvaluatorTests
{
    private readonly AlertEvaluator _evaluator = new();
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BoundaryAndDurationMustBothBeSatisfied()
    {
        var rule = Rule(ComparisonOperator.GreaterThanOrEqual, 80, 10, 60);
        Assert.Empty(_evaluator.Evaluate([rule], Snapshot(80, Start)));
        Assert.Empty(_evaluator.Evaluate([rule], Snapshot(80, Start.AddSeconds(9))));
        Assert.Single(_evaluator.Evaluate([rule], Snapshot(80, Start.AddSeconds(10))));
    }

    [Fact]
    public void ContinuousBreachFiresOnceEvenAfterCooldown()
    {
        var rule = Rule(ComparisonOperator.GreaterThan, 80, 0, 5);
        Assert.Single(_evaluator.Evaluate([rule], Snapshot(81, Start)));
        Assert.Empty(_evaluator.Evaluate([rule], Snapshot(82, Start.AddSeconds(10))));
    }

    [Fact]
    public void RecoveryAllowsRetriggerAfterCooldown()
    {
        var rule = Rule(ComparisonOperator.GreaterThan, 80, 0, 5);
        Assert.Single(_evaluator.Evaluate([rule], Snapshot(81, Start)));
        Assert.Empty(_evaluator.Evaluate([rule], Snapshot(70, Start.AddSeconds(1))));
        Assert.Single(_evaluator.Evaluate([rule], Snapshot(81, Start.AddSeconds(6))));
    }

    [Fact]
    public void DisabledAndMissingReadingsNeverTrigger()
    {
        var disabled = Rule(ComparisonOperator.LessThanOrEqual, 80, 0, 0) with { Enabled = false };
        Assert.Empty(_evaluator.Evaluate([disabled], Snapshot(20, Start)));
        Assert.Empty(_evaluator.Evaluate([Rule(ComparisonOperator.GreaterThan, 1, 0, 0)], MetricSnapshot.Empty(Start)));
    }

    private static AlertRule Rule(ComparisonOperator op, double threshold, int durationSeconds, int cooldownSeconds) =>
        new(Id, "Test", MetricKind.CpuTemperature, op, threshold, TimeSpan.FromSeconds(durationSeconds),
            TimeSpan.FromSeconds(cooldownSeconds), AlertSeverity.Warning);

    private static MetricSnapshot Snapshot(double value, DateTimeOffset at) => new(at,
        new Dictionary<MetricKind, MetricReading>
        {
            [MetricKind.CpuTemperature] = new(MetricKind.CpuTemperature, value, "°C", at, false, "fixture", "Fixture", null)
        });
}
