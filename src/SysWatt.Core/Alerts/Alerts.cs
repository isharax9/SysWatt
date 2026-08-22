using SysWatt.Core.Sensors;

namespace SysWatt.Core.Alerts;

public enum ComparisonOperator { GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual }
public enum AlertSeverity { Info, Warning, Critical }

public sealed record AlertRule(
    Guid Id,
    string Name,
    MetricKind Metric,
    ComparisonOperator Operator,
    double Threshold,
    TimeSpan RequiredDuration,
    TimeSpan Cooldown,
    AlertSeverity Severity,
    bool Enabled = true,
    bool ShowDesktopNotification = true,
    bool ShowInApp = true)
{
    public static AlertRule CreateDefault() => new(Guid.NewGuid(), "CPU temperature high", MetricKind.CpuTemperature,
        ComparisonOperator.GreaterThanOrEqual, 85, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), AlertSeverity.Warning);
}

public sealed record AlertEvent(AlertRule Rule, double Value, DateTimeOffset Timestamp, string Message);

public interface IAlertEvaluator
{
    IReadOnlyList<AlertEvent> Evaluate(IEnumerable<AlertRule> rules, MetricSnapshot snapshot);
    void Reset(Guid ruleId);
}

public sealed class AlertEvaluator : IAlertEvaluator
{
    private sealed class State
    {
        public DateTimeOffset? BreachStarted { get; set; }
        public DateTimeOffset? LastTriggered { get; set; }
        public bool TriggeredDuringBreach { get; set; }
    }

    private readonly Dictionary<Guid, State> _states = [];

    public IReadOnlyList<AlertEvent> Evaluate(IEnumerable<AlertRule> rules, MetricSnapshot snapshot)
    {
        var events = new List<AlertEvent>();
        foreach (var rule in rules)
        {
            if (!_states.TryGetValue(rule.Id, out var state)) _states[rule.Id] = state = new State();
            var reading = snapshot[rule.Metric];
            if (!rule.Enabled || !reading.IsAvailable)
            {
                state.BreachStarted = null;
                state.TriggeredDuringBreach = false;
                continue;
            }

            var value = reading.Value!.Value;
            if (!Compare(value, rule.Operator, rule.Threshold))
            {
                state.BreachStarted = null;
                state.TriggeredDuringBreach = false;
                continue;
            }

            state.BreachStarted ??= snapshot.Timestamp;
            var sustained = snapshot.Timestamp - state.BreachStarted.Value >= rule.RequiredDuration;
            var cooledDown = state.LastTriggered is null || snapshot.Timestamp - state.LastTriggered.Value >= rule.Cooldown;
            if (sustained && cooledDown && !state.TriggeredDuringBreach)
            {
                state.LastTriggered = snapshot.Timestamp;
                state.TriggeredDuringBreach = true;
                events.Add(new AlertEvent(rule, value, snapshot.Timestamp,
                    $"{rule.Name}: {rule.Metric} is {value:0.#} {MetricUnits.For(rule.Metric)}."));
            }
        }
        return events;
    }

    public void Reset(Guid ruleId) => _states.Remove(ruleId);

    private static bool Compare(double value, ComparisonOperator op, double threshold) => op switch
    {
        ComparisonOperator.GreaterThan => value > threshold,
        ComparisonOperator.GreaterThanOrEqual => value >= threshold,
        ComparisonOperator.LessThan => value < threshold,
        ComparisonOperator.LessThanOrEqual => value <= threshold,
        _ => false
    };
}
