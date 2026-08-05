using Data.Metrics.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IDbStorageHistoryQueryService
{
    Task<DbStorageHistoryReadModel> GetAsync(int? windowDays, CancellationToken ct);
}

/// <summary>
/// Shapes the daily storage snapshots (#925) into the admin panel's charts and
/// forecast. A thin adapter over <see cref="IDbStorageSnapshotStore"/>, on the same
/// lines as the other Mongo-backed ops services: the store owns the query, this owns
/// the arithmetic, and the arithmetic that matters (the projection) lives in the pure
/// <see cref="StorageForecastCalculator"/> so it can be tested without a database.
/// </summary>
public sealed class DbStorageHistoryQueryService(
    IDbStorageSnapshotStore store,
    IOptions<StorageHistoryOptions> options,
    TimeProvider timeProvider) : IDbStorageHistoryQueryService
{
    // Hard ceiling on the requested window, mirroring RankHistoryQueryService. Two
    // reasons: the retention TTL means there is nothing older than a year to read
    // anyway, and — the sharp edge — an unbounded `windowDays` (?windowDays=999999999)
    // overflows DateTime.AddDays and 500s the whole endpoint rather than the caller's
    // own request being merely silly.
    private const int MaxWindowDays = 730;

    public async Task<DbStorageHistoryReadModel> GetAsync(int? windowDays, CancellationToken ct)
    {
        var settings = options.Value;
        var days = Math.Clamp(
            windowDays is > 0 ? windowDays.Value : settings.DefaultWindowDays,
            1,
            MaxWindowDays);
        var since = timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(-days);

        var points = await store.GetHistoryAsync(since, ct);
        if (points.Count == 0)
        {
            // No snapshots yet — the step has not run, or Mongo is unconfigured. An
            // empty model is the right answer; the panel says "no history yet".
            return new DbStorageHistoryReadModel();
        }

        var daily = points
            .GroupBy(point => point.SnapshotDateUtc)
            .OrderBy(group => group.Key)
            .Select(group => new DbStorageDailyPoint
            {
                DateUtc = group.Key,
                // Sum across engines, never Max: DatabaseBytes is denormalised onto
                // every row of a day *for its own engine*, so the day carries one
                // figure per engine and both sit on the same volume (#1023). Max would
                // silently report whichever engine is larger as the disk total. Within
                // an engine it is still Max rather than First, which guards a
                // partially-written day.
                DatabaseBytes = SumPerEngine(group),
                PostgresBytes = EngineBytes(group, StorageEngines.Postgres),
                MongoBytes = EngineBytes(group, StorageEngines.Mongo),
                TotalBytes = group.Sum(point => point.TotalBytes),
                RowEstimate = group.Sum(point => point.RowEstimate),
            })
            .ToList();

        var latestDate = daily[^1].DateUtc;
        var currentSizes = points
            .Where(point => point.SnapshotDateUtc == latestDate)
            .ToDictionary(point => (point.Engine, point.TableName), point => point.TotalBytes);

        // Keyed on (engine, name), not name alone: process_runs and seed_requests
        // exist on both sides, and collapsing them would add a Postgres table's size
        // to a Mongo collection's.
        var topTables = currentSizes
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key.TableName, StringComparer.Ordinal)
            .Take(Math.Max(1, settings.TopTables))
            .Select(entry => entry.Key)
            .ToHashSet();

        var tables = points
            .Where(point => topTables.Contains((point.Engine, point.TableName)))
            .GroupBy(point => (point.Engine, point.TableName))
            .Select(group => BuildSeries(
                group.Key.Engine, group.Key.TableName, [.. group.OrderBy(point => point.SnapshotDateUtc)]))
            .OrderByDescending(series => series.CurrentBytes)
            .ThenBy(series => series.TableName, StringComparer.Ordinal)
            .ToList();

        var engines = points
            .Select(point => point.Engine)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(engine => engine, StringComparer.Ordinal)
            .ToList();

        var comparable = ComparableDays(points, daily);

        return new DbStorageHistoryReadModel
        {
            Daily = daily,
            Tables = tables,
            Engines = engines,
            ComparableDays = comparable.Count,
            Forecast = BuildForecast(comparable, settings),
        };
    }

    /// <summary>
    /// The day's disk footprint: one reading per engine, summed. Both engines share a
    /// volume, so the disk total is their sum.
    /// </summary>
    private static long SumPerEngine(IEnumerable<DbTableSizeSnapshotPoint> day)
        => day
            .GroupBy(point => point.Engine, StringComparer.Ordinal)
            .Sum(engine => engine.Max(point => point.DatabaseBytes));

    private static long EngineBytes(IEnumerable<DbTableSizeSnapshotPoint> day, string engine)
    {
        var rows = day.Where(point => string.Equals(point.Engine, engine, StringComparison.Ordinal)).ToList();
        return rows.Count == 0 ? 0 : rows.Max(point => point.DatabaseBytes);
    }

    /// <summary>
    /// The trailing days that measure the same set of engines as the most recent one.
    ///
    /// <para>
    /// The day Mongo first gets measured (#1023) adds its whole footprint at once, and
    /// that step is not growth — fitting a trend across it would read the one-off jump
    /// as a daily rate and forecast a saturation that is not coming. Rather than
    /// splice the series or backfill a number nobody measured, the forecast simply
    /// starts again from the first comparable day and stays absent until three of them
    /// exist, which is the rule the panel already explains.
    /// </para>
    /// </summary>
    private static List<DbStorageDailyPoint> ComparableDays(
        IReadOnlyList<DbTableSizeSnapshotPoint> points,
        List<DbStorageDailyPoint> daily)
    {
        var enginesByDay = points
            .GroupBy(point => point.SnapshotDateUtc)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(point => point.Engine)
                    .ToHashSet(StringComparer.Ordinal));

        if (!enginesByDay.TryGetValue(daily[^1].DateUtc, out var latestEngines))
        {
            return daily;
        }

        var comparable = new List<DbStorageDailyPoint>(daily.Count);
        for (var index = daily.Count - 1; index >= 0; index--)
        {
            if (!enginesByDay.TryGetValue(daily[index].DateUtc, out var engines)
                || !engines.SetEquals(latestEngines))
            {
                break;
            }

            comparable.Add(daily[index]);
        }

        comparable.Reverse();
        return comparable;
    }

    private static DbStorageTableSeries BuildSeries(
        string engine,
        string tableName,
        IReadOnlyList<DbTableSizeSnapshotPoint> ordered)
    {
        var first = ordered[0];
        var last = ordered[^1];
        var spanDays = (last.SnapshotDateUtc - first.SnapshotDateUtc).TotalDays;

        // A single day in the window gives no rate at all — reporting the absolute
        // size as "per day" would overstate growth by the whole history.
        var bytesPerDay = spanDays > 0 ? (long)Math.Round((last.TotalBytes - first.TotalBytes) / spanDays) : 0;
        var rowsPerDay = spanDays > 0 ? (long)Math.Round((last.RowEstimate - first.RowEstimate) / spanDays) : 0;

        return new DbStorageTableSeries
        {
            Engine = engine,
            TableName = tableName,
            Points = [.. ordered.Select(point => new DbStorageTablePoint
            {
                DateUtc = point.SnapshotDateUtc,
                TotalBytes = point.TotalBytes,
                RowEstimate = point.RowEstimate,
            })],
            CurrentBytes = last.TotalBytes,
            BytesPerDay = bytesPerDay,
            RowsPerDay = rowsPerDay,
            // Undefined rather than infinite when the table started empty.
            GrowthRate = first.TotalBytes > 0
                ? (double)(last.TotalBytes - first.TotalBytes) / first.TotalBytes
                : null,
        };
    }

    private static DbStorageForecast? BuildForecast(
        IReadOnlyList<DbStorageDailyPoint> daily,
        StorageHistoryOptions settings)
    {
        // Without a configured volume size there is nothing to be a percentage of, and
        // a forecast against a guessed capacity is worse than no forecast.
        if (settings.DiskCapacityBytes <= 0)
        {
            return null;
        }

        var thresholds = settings.ThresholdPercents
            .Where(percent => percent > 0)
            .Select(percent => (Percent: percent, Bytes: (long)(settings.DiskCapacityBytes * percent / 100)))
            .OrderBy(threshold => threshold.Bytes)
            .ToList();

        if (thresholds.Count == 0)
        {
            return null;
        }

        var forecast = StorageForecastCalculator.Project(
            [.. daily.Select(point => new StorageForecastPoint(point.DateUtc, point.DatabaseBytes))],
            [.. thresholds.Select(threshold => threshold.Bytes)]);

        if (forecast is null)
        {
            return null;
        }

        return new DbStorageForecast
        {
            BytesPerDay = forecast.BytesPerDay,
            DiskCapacityBytes = settings.DiskCapacityBytes,
            Crossings = [.. forecast.Crossings.Select((crossing, index) => new DbStorageThresholdCrossing
            {
                Percent = thresholds[index].Percent,
                ThresholdBytes = crossing.ThresholdBytes,
                ProjectedAtUtc = crossing.ProjectedAtUtc,
            })],
        };
    }
}
