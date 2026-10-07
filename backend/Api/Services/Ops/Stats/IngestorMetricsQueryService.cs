using Data.Logging.Mongo;
using Data.Metrics;
using Data.Metrics.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Stats;

public interface IIngestorMetricsQueryService
{
    /// <summary>
    /// The Ingestor meter's instruments over the relative <paramref name="window"/>
    /// (<c>1h</c> / <c>24h</c> / <c>7d</c> / <c>30d</c>; anything else is 24h, like
    /// <c>/ops/riot-usage</c>).
    /// </summary>
    Task<IngestorMetricsReadModel> GetAsync(string? window, CancellationToken ct);
}

/// <summary>
/// Reads the Ingestor meter's rollups (#1636) and groups them per instrument. The store sums
/// each series over the window; the per-instrument totals and the means are made here.
/// </summary>
public sealed class IngestorMetricsQueryService(
    IMeterRollupStore store,
    TimeProvider timeProvider,
    IOptions<MongoLoggingOptions> mongoOptions) : IIngestorMetricsQueryService
{
    public async Task<IngestorMetricsReadModel> GetAsync(string? window, CancellationToken ct)
    {
        var (resolved, key) = RiotApiUsageQueryService.ResolveWindow(window);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var since = resolved switch
        {
            RiotUsageWindow.LastHour => now.AddHours(-1),
            RiotUsageWindow.Last7Days => now.AddDays(-7),
            RiotUsageWindow.Last30Days => now.AddDays(-30),
            _ => now.AddHours(-24)
        };

        var rollups = await store.GetWindowAsync(MeterNames.Ingestor, since, ct);
        var retention = mongoOptions.Value.MeterRollupsRetention;

        return new IngestorMetricsReadModel
        {
            Window = key,
            SinceUtc = since,
            GeneratedAtUtc = now,
            RetentionDays = retention > TimeSpan.Zero ? retention.TotalDays : null,
            OldestRetainedUtc = rollups.OldestRetainedBucketUtc,
            Instruments = BuildInstruments(rollups.Series)
        };
    }

    internal static IReadOnlyList<IngestorInstrumentReadModel> BuildInstruments(IReadOnlyList<MeterSeriesTotal> series)
        => series
            .GroupBy(item => item.Instrument, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new IngestorInstrumentReadModel
            {
                Name = group.Key,
                Kind = group.First().Kind,
                Unit = group.Select(item => item.Unit).FirstOrDefault(unit => unit is not null),
                Description = group.Select(item => item.Description).FirstOrDefault(text => text is not null),
                Count = group.Sum(item => item.Count),
                Sum = group.Sum(item => item.Sum),
                Max = group.Max(item => item.Max),
                Series = group
                    .OrderByDescending(item => item.Sum)
                    .ThenByDescending(item => item.Count)
                    .Select(item => new IngestorMeterSeriesReadModel
                    {
                        Tags = item.Tags,
                        Count = item.Count,
                        Sum = item.Sum,
                        Mean = item.Count > 0 ? item.Sum / item.Count : 0,
                        Max = item.Max,
                        LastRecordedAtUtc = item.LastRecordedAtUtc
                    })
                    .ToList()
            })
            .ToList();
}
