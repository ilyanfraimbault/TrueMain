using Core.Options;
using Data;
using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The automated anomaly detectors behind the admin data-quality panel (#924).
///
/// <para>
/// Read-only: every detector measures and judges, none of them repairs. The duplicate
/// dimension rows it counts are not repairable here because they are no longer
/// creatable: the schema enforces each dimension's canonical identity (#1418), and this
/// card groups on the very expressions those constraints are built from — see
/// <c>Data.DataQuality.ChampionDimensionCanonicalKeys</c> — so a non-zero count means a
/// constraint went missing, not that a repair is owed.
/// </para>
/// </summary>
public interface IDataQualityDetectorsQueryService
{
    /// <summary>
    /// Runs every detector and returns one card each. A detector whose query fails
    /// reports <c>unknown</c> with the reason rather than failing the whole panel.
    /// </summary>
    Task<DataQualityDetectorsReadModel> GetDetectorsAsync(CancellationToken ct);

    /// <summary>
    /// The on-demand per-champion aggregate-freshness breakdown, kept off the page-load
    /// payload because it is the one measurement needing a grouped scan of
    /// <c>champion_aggregate_scopes</c>.
    /// </summary>
    Task<AggregateFreshnessReadModel> GetAggregateFreshnessAsync(CancellationToken ct);
}

/// <summary>
/// Measures the data-quality detectors (#924). The judgement lives in the pure
/// <see cref="DataQualityDetectorEvaluator"/>; each detector family's questions to the
/// database and the wording of its card live in its own class
/// (<see cref="DuplicateDimensionsDetector"/>, <see cref="AggregateFreshnessDetector"/>,
/// <see cref="OrphanParticipantsDetector"/>, <see cref="IngestionLagDetector"/>,
/// <see cref="RowSanityDetector"/>); this class only runs them and contains their failures.
///
/// <para>
/// <b>Cost.</b> The panel loads on a page view, so no detector is allowed a scan it
/// cannot afford. The duplicate detector groups the <c>champion_dim_*</c> tables (tens
/// of thousands of rows), the orphan detector samples the newest matches per platform
/// through <c>IX_matches_platform_queue_game_start</c> rather than ratioing the whole
/// <c>match_participants</c> table, and the per-champion freshness breakdown — the one
/// genuinely grouped scan — is a separate endpoint behind an explicit click, the same
/// split #925 made for storage. The remaining counts are of the same order as the ones
/// <see cref="OverviewQueryService"/> already runs on the overview panel.
/// </para>
///
/// <para>
/// <b>Failure is not a pass.</b> Each detector is measured independently and a query
/// that throws yields <c>unknown</c> with the reason attached, never green and never a
/// 500 for the whole panel: one broken detector must not blind the other four. The
/// detectors share one <see cref="TrueMainDbContext"/>, so they run one after another.
/// </para>
/// </summary>
public sealed class DataQualityDetectorsQueryService(
    TrueMainDbContext db,
    IProcessRunStore processRunStore,
    IOptions<DataQualityDetectorOptions> options,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    TimeProvider timeProvider,
    ILogger<DataQualityDetectorsQueryService> logger) : IDataQualityDetectorsQueryService
{
    private readonly AggregateFreshnessDetector _aggregateFreshness = new(db, processRunStore);

    public async Task<DataQualityDetectorsReadModel> GetDetectorsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settings = options.Value;

        var detectors = new List<DataQualityDetectorReadModel>
        {
            await SafeAsync(
                "duplicateDimensionRows",
                "Duplicate dimension rows",
                () => new DuplicateDimensionsDetector(db).BuildAsync(settings, ct),
                ct),
            await SafeAsync(
                "aggregateFreshness",
                "Aggregate freshness",
                () => _aggregateFreshness.BuildAsync(settings, now, ct),
                ct),
            await SafeAsync(
                "orphanParticipants",
                "Orphan participants & harvest",
                () => new OrphanParticipantsDetector(db, processRunStore, mainAnalysisOptions)
                    .BuildAsync(settings, now, ct),
                ct),
            await SafeAsync(
                "ingestionLag",
                "Ingestion lag & queues",
                () => new IngestionLagDetector(db, mainAnalysisOptions).BuildAsync(settings, now, ct),
                ct),
            await SafeAsync(
                "rowSanity",
                "Row-level sanity",
                () => new RowSanityDetector(db, mainAnalysisOptions).BuildAsync(settings, ct),
                ct)
        };

        return new DataQualityDetectorsReadModel
        {
            Detectors = detectors,
            EvaluatedAtUtc = now
        };
    }

    public Task<AggregateFreshnessReadModel> GetAggregateFreshnessAsync(CancellationToken ct)
        => _aggregateFreshness.GetBreakdownAsync(options.Value, timeProvider.GetUtcNow().UtcDateTime, ct);

    private async Task<DataQualityDetectorReadModel> SafeAsync(
        string key,
        string title,
        Func<Task<DataQualityDetectorReadModel>> build,
        CancellationToken ct)
    {
        try
        {
            return await build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One detector's broken query must not blind the other four, and must not
            // read as green either.
            logger.LogWarning(ex, "Data-quality detector {Detector} failed to measure", key);

            return new DataQualityDetectorReadModel
            {
                Key = key,
                Title = title,
                Status = DetectorStatus.Unknown.ToWireName(),
                CountLabel = string.Empty,
                Headline = "This detector could not be measured.",
                UnknownReason = ex.Message,
                SourceNote = "The measurement failed; the panel reports unknown rather than a pass."
            };
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }
}
