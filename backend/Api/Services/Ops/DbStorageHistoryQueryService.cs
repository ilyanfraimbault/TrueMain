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
    public async Task<DbStorageHistoryReadModel> GetAsync(int? windowDays, CancellationToken ct)
    {
        var settings = options.Value;
        var days = windowDays is > 0 ? windowDays.Value : settings.DefaultWindowDays;
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
                // DatabaseBytes is denormalised onto every row of a day, so any row
                // carries it; Max rather than First guards a partially-written day.
                DatabaseBytes = group.Max(point => point.DatabaseBytes),
                TotalBytes = group.Sum(point => point.TotalBytes),
                RowEstimate = group.Sum(point => point.RowEstimate),
            })
            .ToList();

        var latestDate = daily[^1].DateUtc;
        var currentSizes = points
            .Where(point => point.SnapshotDateUtc == latestDate)
            .ToDictionary(point => point.TableName, point => point.TotalBytes, StringComparer.Ordinal);

        var topTables = currentSizes
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, settings.TopTables))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var tables = points
            .Where(point => topTables.Contains(point.TableName))
            .GroupBy(point => point.TableName, StringComparer.Ordinal)
            .Select(group => BuildSeries(group.Key, [.. group.OrderBy(point => point.SnapshotDateUtc)]))
            .OrderByDescending(series => series.CurrentBytes)
            .ThenBy(series => series.TableName, StringComparer.Ordinal)
            .ToList();

        return new DbStorageHistoryReadModel
        {
            Daily = daily,
            Tables = tables,
            Forecast = BuildForecast(daily, settings),
        };
    }

    private static DbStorageTableSeries BuildSeries(string tableName, IReadOnlyList<DbTableSizeSnapshotPoint> ordered)
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
