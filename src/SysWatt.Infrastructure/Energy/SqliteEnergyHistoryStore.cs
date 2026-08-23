using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SysWatt.Core.Energy;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Energy;

public sealed class SqliteEnergyHistoryStore : IEnergyHistoryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private DateTimeOffset? _lastTimestamp;
    private double? _lastWatts;
    private TelemetrySource? _lastSource;

    public string DatabasePath { get; }

    public SqliteEnergyHistoryStore() : this(null) { }

    public SqliteEnergyHistoryStore(string? rootOverride)
    {
        var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
        var root = rootOverride ?? (portable
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysWatt"));
        DatabasePath = Path.Combine(root, "energy-history.db");
    }

    public Task RecordSampleAsync(DateTimeOffset timestamp, double watts, CancellationToken cancellationToken = default) =>
        RecordSampleAsync(timestamp, watts, TelemetrySource.Standalone, cancellationToken);

    public async Task RecordSampleAsync(DateTimeOffset timestamp, double watts, TelemetrySource source, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(watts) || watts < 0 || watts > 100_000) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (_lastTimestamp is { } previous && _lastWatts is { } previousWatts)
            {
                var elapsed = timestamp - previous;
                if (elapsed > TimeSpan.Zero && elapsed <= TimeSpan.FromMinutes(5))
                {
                    var wattHours = ((previousWatts + watts) / 2d) * elapsed.TotalHours;
                    await AddIntervalAsync(timestamp, watts, wattHours, elapsed.TotalHours, _lastSource ?? source, cancellationToken).ConfigureAwait(false);
                }
            }
            _lastTimestamp = timestamp;
            _lastWatts = watts;
            _lastSource = source;
        }
        finally { _gate.Release(); }
    }

    public async Task<DailyEnergySummary> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var rows = await GetRangeAsync(date, date, cancellationToken).ConfigureAwait(false);
        return rows[0];
    }

    public async Task<IReadOnlyList<DailyEnergySummary>> GetRangeAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
    {
        if (through < from) (from, through) = (through, from);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT local_date, watt_hours, average_watts, peak_watts FROM daily_energy WHERE local_date >= $from AND local_date <= $through ORDER BY local_date";
            command.Parameters.AddWithValue("$from", DateKey(from));
            command.Parameters.AddWithValue("$through", DateKey(through));
            var found = new Dictionary<DateOnly, DailyEnergySummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var date = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                found[date] = new(date, reader.GetDouble(1) / 1000d, reader.GetDouble(2), reader.GetDouble(3)) { HasData = true };
            }
            var sourceByDate = new Dictionary<DateOnly, Dictionary<TelemetrySource, double>>();
            await using (var sourceCommand = connection.CreateCommand())
            {
                sourceCommand.CommandText = "SELECT local_date, source, watt_hours FROM daily_energy_source WHERE local_date >= $from AND local_date <= $through";
                sourceCommand.Parameters.AddWithValue("$from", DateKey(from));
                sourceCommand.Parameters.AddWithValue("$through", DateKey(through));
                await using var sourceReader = await sourceCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await sourceReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var date = DateOnly.ParseExact(sourceReader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (!Enum.TryParse<TelemetrySource>(sourceReader.GetString(1), out var source)) continue;
                    if (!sourceByDate.TryGetValue(date, out var values)) sourceByDate[date] = values = new();
                    values[source] = sourceReader.GetDouble(2) / 1000d;
                }
            }
            var result = new List<DailyEnergySummary>();
            for (var day = from; day <= through; day = day.AddDays(1))
            {
                var value = found.TryGetValue(day, out var summary) ? summary : new(day, 0, 0, 0);
                result.Add(value with { KilowattHoursBySource = sourceByDate.TryGetValue(day, out var sources) ? sources : new Dictionary<TelemetrySource, double>() });
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var days = new List<EnergyArchiveDay>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT local_date, watt_hours, average_watts, peak_watts FROM daily_energy ORDER BY local_date";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                days.Add(new(reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));

            var archive = new EnergyArchive(1, DateTimeOffset.UtcNow, days);
            var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, archive, ArchiveJson, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var archive = await JsonSerializer.DeserializeAsync<EnergyArchive>(stream, ArchiveJson, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The energy archive is empty.");
        if (archive.Version != 1) throw new InvalidDataException($"Energy archive version {archive.Version} is not supported.");
        var valid = archive.Days.Select(ValidateArchiveDay).ToArray();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var day in valid)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO daily_energy(local_date, watt_hours, weighted_watts, duration_hours, average_watts, peak_watts)
                    VALUES($date, $wh, $weighted, $hours, $average, $peak)
                    ON CONFLICT(local_date) DO UPDATE SET
                      watt_hours = excluded.watt_hours,
                      weighted_watts = excluded.weighted_watts,
                      duration_hours = excluded.duration_hours,
                      average_watts = excluded.average_watts,
                      peak_watts = excluded.peak_watts;
                    DELETE FROM daily_energy_source WHERE local_date = $date;
                    INSERT INTO daily_energy_source(local_date, source, watt_hours, weighted_watts, duration_hours, average_watts, peak_watts)
                    VALUES($date, 'Imported', $wh, $weighted, $hours, $average, $peak);
                    """;
                var hours = day.AverageWatts > 0 ? day.WattHours / day.AverageWatts : 0;
                command.Parameters.AddWithValue("$date", day.Date);
                command.Parameters.AddWithValue("$wh", day.WattHours);
                command.Parameters.AddWithValue("$weighted", day.WattHours);
                command.Parameters.AddWithValue("$hours", hours);
                command.Parameters.AddWithValue("$average", day.AverageWatts);
                command.Parameters.AddWithValue("$peak", day.PeakWatts);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _lastTimestamp = null;
            _lastWatts = null;
            _lastSource = null;
            return valid.Length;
        }
        finally { _gate.Release(); }
    }

    private static EnergyArchiveDay ValidateArchiveDay(EnergyArchiveDay day)
    {
        if (!DateOnly.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException($"Invalid archive date: {day.Date}");
        if (!double.IsFinite(day.WattHours) || day.WattHours is < 0 or > 2_400_000 ||
            !double.IsFinite(day.AverageWatts) || day.AverageWatts is < 0 or > 100_000 ||
            !double.IsFinite(day.PeakWatts) || day.PeakWatts is < 0 or > 100_000)
            throw new InvalidDataException($"Invalid energy values for {day.Date}.");
        return day;
    }

    private async Task AddIntervalAsync(DateTimeOffset timestamp, double watts, double wattHours, double elapsedHours, TelemetrySource source, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var localDate = DateOnly.FromDateTime(timestamp.LocalDateTime);
        await using (var daily = connection.CreateCommand())
        {
            daily.Transaction = (SqliteTransaction)transaction;
            daily.CommandText = """
                INSERT INTO daily_energy(local_date, watt_hours, weighted_watts, duration_hours, average_watts, peak_watts)
                VALUES($date, $wh, $weighted, $hours, $watts, $watts)
                ON CONFLICT(local_date) DO UPDATE SET
                  watt_hours = watt_hours + excluded.watt_hours,
                  weighted_watts = weighted_watts + excluded.weighted_watts,
                  duration_hours = duration_hours + excluded.duration_hours,
                  average_watts = (weighted_watts + excluded.weighted_watts) / MAX(duration_hours + excluded.duration_hours, 0.0000001),
                  peak_watts = MAX(peak_watts, excluded.peak_watts)
                """;
            var hours = elapsedHours;
            daily.Parameters.AddWithValue("$date", DateKey(localDate));
            daily.Parameters.AddWithValue("$wh", wattHours);
            daily.Parameters.AddWithValue("$weighted", wattHours);
            daily.Parameters.AddWithValue("$hours", hours);
            daily.Parameters.AddWithValue("$watts", watts);
            await daily.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var minute = connection.CreateCommand())
        {
            minute.Transaction = (SqliteTransaction)transaction;
            var bucket = timestamp.ToUnixTimeSeconds() / 60 * 60;
            minute.CommandText = """
                INSERT INTO minute_energy(minute_utc, average_watts, watt_hours, samples)
                VALUES($minute, $watts, $wh, 1)
                ON CONFLICT(minute_utc) DO UPDATE SET
                  average_watts = ((average_watts * samples) + excluded.average_watts) / (samples + 1),
                  watt_hours = watt_hours + excluded.watt_hours,
                  samples = samples + 1
                """;
            minute.Parameters.AddWithValue("$minute", bucket);
            minute.Parameters.AddWithValue("$watts", watts);
            minute.Parameters.AddWithValue("$wh", wattHours);
            await minute.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var bySource = connection.CreateCommand())
        {
            bySource.Transaction = (SqliteTransaction)transaction;
            bySource.CommandText = """
                INSERT INTO daily_energy_source(local_date, source, watt_hours, weighted_watts, duration_hours, average_watts, peak_watts)
                VALUES($date, $source, $wh, $weighted, $hours, $watts, $watts)
                ON CONFLICT(local_date, source) DO UPDATE SET
                  watt_hours = watt_hours + excluded.watt_hours,
                  weighted_watts = weighted_watts + excluded.weighted_watts,
                  duration_hours = duration_hours + excluded.duration_hours,
                  average_watts = (weighted_watts + excluded.weighted_watts) / MAX(duration_hours + excluded.duration_hours, 0.0000001),
                  peak_watts = MAX(peak_watts, excluded.peak_watts)
                """;
            bySource.Parameters.AddWithValue("$date", DateKey(DateOnly.FromDateTime(timestamp.LocalDateTime)));
            bySource.Parameters.AddWithValue("$source", source.ToString());
            bySource.Parameters.AddWithValue("$wh", wattHours);
            bySource.Parameters.AddWithValue("$weighted", wattHours);
            bySource.Parameters.AddWithValue("$hours", elapsedHours);
            bySource.Parameters.AddWithValue("$watts", watts);
            await bySource.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS daily_energy(
              local_date TEXT PRIMARY KEY,
              watt_hours REAL NOT NULL DEFAULT 0,
              weighted_watts REAL NOT NULL DEFAULT 0,
              duration_hours REAL NOT NULL DEFAULT 0,
              average_watts REAL NOT NULL DEFAULT 0,
              peak_watts REAL NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS minute_energy(
              minute_utc INTEGER PRIMARY KEY,
              average_watts REAL NOT NULL,
              watt_hours REAL NOT NULL,
              samples INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_minute_energy_time ON minute_energy(minute_utc);
            CREATE TABLE IF NOT EXISTS daily_energy_source(
              local_date TEXT NOT NULL,
              source TEXT NOT NULL,
              watt_hours REAL NOT NULL DEFAULT 0,
              weighted_watts REAL NOT NULL DEFAULT 0,
              duration_hours REAL NOT NULL DEFAULT 0,
              average_watts REAL NOT NULL DEFAULT 0,
              peak_watts REAL NOT NULL DEFAULT 0,
              PRIMARY KEY(local_date, source));
            CREATE INDEX IF NOT EXISTS ix_daily_energy_source_date ON daily_energy_source(local_date);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string DateKey(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly JsonSerializerOptions ArchiveJson = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private sealed record EnergyArchive(int Version, DateTimeOffset ExportedAt, IReadOnlyList<EnergyArchiveDay> Days);
    private sealed record EnergyArchiveDay(string Date, double WattHours, double AverageWatts, double PeakWatts);

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
