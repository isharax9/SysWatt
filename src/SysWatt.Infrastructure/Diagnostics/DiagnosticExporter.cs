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
                r.Descriptor.SensorId, r.Descriptor.Unit, r.Value, r.Timestamp, r.IsAvailable, r.Error
            }),
            mappings = normalized.Metrics.Values,
            fans = normalized.Fans
        };
        await using var stream = File.Create(path);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        await JsonSerializer.SerializeAsync(stream, report, options, cancellationToken);
    }
}
