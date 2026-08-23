using System.Text.Json;
using System.Text.Json.Serialization;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Diagnostics;

public interface IDiagnosticExporter
{
    Task ExportAsync(string path, IReadOnlyList<RawSensorReading> raw, MetricSnapshot normalized, CancellationToken cancellationToken = default);
}

public sealed class DiagnosticExporter : IDiagnosticExporter
{
    public async Task ExportAsync(string path, IReadOnlyList<RawSensorReading> raw, MetricSnapshot normalized, CancellationToken cancellationToken = default)
    {
        var report = new
        {
            schemaVersion = 3,
            generatedAtUtc = DateTimeOffset.UtcNow,
            appVersion = typeof(DiagnosticExporter).Assembly.GetName().Version?.ToString(),
            operatingSystem = Environment.OSVersion.VersionString,
            telemetrySource = normalized.Source,
            telemetrySourceDiagnostic = normalized.SourceDiagnostic,
            sensors = raw.Select(r => new
            {
                r.Descriptor.Provider, r.Descriptor.HardwareKind, r.Descriptor.HardwareName,
                r.Descriptor.HardwareId, r.Descriptor.SensorKind, r.Descriptor.SensorName,
                r.Descriptor.SensorId, r.Descriptor.Unit,
                Value = r.Value is { } value && double.IsFinite(value) ? value : (double?)null,
                r.Timestamp, r.IsAvailable,
                Error = r.Value is { } invalid && !double.IsFinite(invalid) ? "Provider returned a non-finite value." : r.Error
            }),
            mappings = normalized.Metrics.Values,
            fans = normalized.Fans
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        // Materialize before opening the destination so serialization failure cannot leave a plausible-looking partial report.
        var json = JsonSerializer.Serialize(report, options);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }
}
