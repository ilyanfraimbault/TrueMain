using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// The automated anomaly detectors behind the admin data-quality panel (#924).
///
/// <para>
/// Read-only: every detector measures and judges, none of them repairs. The repairs
/// live in the ingestor (e.g. <c>RunePageDeduplicationProcess</c> for the duplicate
/// dimension rows this panel counts), which is why the canonical-key definitions the
/// detector groups on are shared with it rather than restated here — see
/// <c>Data.DataQuality.ChampionDimensionCanonicalKeys</c>.
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
