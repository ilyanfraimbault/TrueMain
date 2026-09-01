using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

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
    IChampionSummariesQueryService summariesQueryService) : IChampionTierListQueryService
{
    public async Task<ChampionTierListReadModel> GetTierListAsync(
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
