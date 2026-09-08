using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Directory;

public interface IChampionTierListQueryService
{
    /// <summary>
    /// Builds the champion meta / tier-list for a single patch
    /// (<paramref name="patch"/> if non-null and canonical, otherwise the
    /// active patch), grouping <c>(champion, position)</c> rows into S/A/B/C/D
    /// tiers by a winRate + pickRate blend. Tiering is computed independently
    /// per position. When <paramref name="position"/> is non-null only that
    /// position is returned; otherwise every position's rows are tiered and
    /// merged into the same buckets. Always returns a model (possibly with no
    /// tiers) so the caller can render its own empty state.
    /// </summary>
    /// <param name="patch">Requested patch; null resolves to the active patch.</param>
    /// <param name="position">Requested Riot team position; null spans every position.</param>
    /// <param name="eloBracket">
    /// Optional elo filter (per <c>Core.Lol.Ranking.EloBracket</c>): <c>ALL</c>,
    /// a bare tier (e.g. <c>GOLD</c> — that tier only), or a <c>TIER_PLUS</c>
    /// form (e.g. <c>GOLD_PLUS</c> — that tier and above). When null or
    /// <c>ALL</c> the tiers are computed from every band; otherwise only the
    /// selected tier(s) feed the winRate / pickRate blend.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="truemainsOnly">
    /// Population the tiers are computed over: mains of each champion (the
    /// default) or every tracked player. Forwarded straight to the summaries
    /// query the tier list is derived from, so a tier and the directory row it
    /// came from always describe the same games.
    /// </param>
    Task<ChampionTierListReadModel> GetTierListAsync(
        string? patch,
        string? position,
        string? eloBracket,
        bool truemainsOnly,
        CancellationToken ct);
}

/// <summary>
/// Shapes the champion meta / tier-list. It owns no SQL of its own, and no
/// tiering of its own either: the winRate / pickRate / games per
/// <c>(champion, position)</c> <b>and</b> the row's <c>Tier</c> / <c>TierScore</c>
/// all come from <see cref="IChampionSummariesQueryService"/>, which reads the
/// real <c>champion_aggregate_scopes</c> rows and already applies the sample
/// floor, the dominant-lane filter, the active-patch resolution, the caching
/// and — since the tier became lane-relative — the per-position call to
/// <see cref="ChampionTierCalculator"/>.
///
/// <para>
/// This service therefore only <b>reshapes</b>: it filters to the requested
/// position and groups the rows into S/A/B/C/D buckets. It deliberately does
/// not re-run the calculator. Re-tiering the same rows with the same options,
/// grouped the same way per position, is a no-op by construction — the
/// position filter only ever drops whole lanes, never rows inside a lane it
/// keeps, so every kept lane's peer set is identical either way. Recomputing
/// was a second implementation of an invariant the calculator's own doc
/// already states (a row's <c>TierScore</c> matches between
/// <c>GET /champions</c> and <c>GET /champions/tierlist</c>), and the only way
/// the two endpoints could ever disagree.
/// </para>
/// </summary>
public sealed class ChampionTierListQueryService(
    IChampionSummariesQueryService summariesQueryService,
    IChampionReadCache cache) : IChampionTierListQueryService
{
    public Task<ChampionTierListReadModel> GetTierListAsync(
        string? patch,
        string? position,
        string? eloBracket,
        bool truemainsOnly,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            $"champions:tierlist:{patch ?? "auto"}:{position ?? "all"}:{eloBracket ?? "all"}"
                + $":{(truemainsOnly ? "truemains" : "everyone")}",
            token => ComputeTierListAsync(patch, position, eloBracket, truemainsOnly, token),
            ct);

    private async Task<ChampionTierListReadModel> ComputeTierListAsync(
        string? patch,
        string? position,
        string? eloBracket,
        bool truemainsOnly,
        CancellationToken ct)
    {
        ChampionSummariesResult result =
            await summariesQueryService.GetAllSummariesAsync(patch, eloBracket, truemainsOnly, ct);
        IReadOnlyList<ChampionSummaryReadModel> summaries = result.Summaries;
        if (summaries.Count == 0)
        {
            // result.PatchVersion is the resolved patch whenever ResolveActivePatchAsync
            // found one, even with zero ranked rows (#972) — a better answer than the
            // raw requested string, which is null whenever the caller asked for "the
            // active patch" rather than a specific one.
            return new ChampionTierListReadModel { PatchVersion = result.PatchVersion, Position = position };
        }

        // Every summary row is pinned to the same resolved patch — result.PatchVersion
        // gives the patch the tiers were actually computed for (which may differ from
        // the requested string when patch was null).
        string? resolvedPatch = result.PatchVersion;

        IEnumerable<ChampionSummaryReadModel> rows = position is null
            ? summaries
            : summaries.Where(summary => summary.Position == position);

        List<ChampionTierGroupReadModel> tiers = rows
            .GroupBy(summary => summary.Tier, StringComparer.Ordinal)
            .OrderBy(group => Array.IndexOf(ChampionTierCalculator.TierOrder, group.Key))
            .Select(group => new ChampionTierGroupReadModel
            {
                Tier = group.Key,
                // Strongest-first within the tier by the same blended score the
                // bucketing used; ChampionId breaks exact-score ties for a
                // stable, deterministic order.
                Entries = group
                    .OrderByDescending(summary => summary.TierScore)
                    .ThenBy(summary => summary.ChampionId)
                    .Select(summary => new ChampionTierEntryReadModel
                    {
                        ChampionId = summary.ChampionId,
                        Position = summary.Position,
                        Games = summary.Games,
                        WinRate = summary.WinRate,
                        PickRate = summary.PickRate,
                        BanRate = summary.BanRate,
                    })
                    .ToList(),
            })
            .ToList();

        return new ChampionTierListReadModel
        {
            PatchVersion = resolvedPatch,
            Position = position,
            Tiers = tiers,
        };
    }
}
