using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

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
