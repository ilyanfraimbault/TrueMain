using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Shapes the champion meta / tier-list. It owns no SQL of its own: the
/// underlying winRate / pickRate / games per <c>(champion, position)</c> all
/// come from <see cref="IChampionSummariesQueryService"/>, which reads the
/// real <c>champion_aggregate_scopes</c> rows (and already applies the sample
/// floor + active-patch resolution + caching). This service only re-tiers
/// those rows <b>per position</b> and groups them into S/A/B/C/D buckets, so
/// the tier reads as "best for this role" rather than the directory's
/// patch-wide tier.
/// </summary>
public sealed class ChampionTierListQueryService(
    IChampionSummariesQueryService summariesQueryService) : IChampionTierListQueryService
{
    // S > A > B > C > D — used to order the emitted tier groups regardless of
    // which letters a sparse field actually produced.
    private static readonly string[] TierOrder =
    [
        ChampionTierCalculator.TierS,
        ChampionTierCalculator.TierA,
        ChampionTierCalculator.TierB,
        ChampionTierCalculator.TierC,
        ChampionTierCalculator.TierD,
    ];

    public async Task<ChampionTierListReadModel> GetTierListAsync(
        string? patch,
        string? position,
        CancellationToken ct)
    {
        var summaries = await summariesQueryService.GetAllSummariesAsync(patch, ct);
        if (summaries.Count == 0)
        {
            return new ChampionTierListReadModel { PatchVersion = patch ?? string.Empty, Position = position };
        }

        // Every summary row is pinned to the same resolved patch, so reading it
        // off the first row gives the patch the tiers were actually computed for
        // (which may differ from the requested string when patch was null).
        var resolvedPatch = summaries[0].PatchVersion;

        var rows = position is null
            ? summaries
            : summaries.Where(summary => summary.Position == position).ToList();

        // Re-tier within each position independently so the bucket reflects how
        // a champion ranks among its role peers, not against the whole patch.
        var scored = rows
            .GroupBy(summary => summary.Position)
            .SelectMany(TierPosition)
            .ToList();

        var tiers = scored
            .GroupBy(entry => entry.Tier)
            .OrderBy(group => Array.IndexOf(TierOrder, group.Key))
            .Select(group => new ChampionTierGroupReadModel
            {
                Tier = group.Key,
                // Strongest-first within the tier by the same blended score the
                // bucketing used; ChampionId breaks exact-score ties for a
                // stable, deterministic order.
                Entries = group
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.Entry.ChampionId)
                    .Select(entry => entry.Entry)
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

    // Tier one position's rows in isolation, carrying the blended score so the
    // caller can order entries within a tier the same way they were bucketed.
    private static IEnumerable<ScoredEntry> TierPosition(IEnumerable<ChampionSummaryReadModel> positionRows)
    {
        var ordered = positionRows.ToList();
        if (ordered.Count == 0)
        {
            yield break;
        }

        var inputs = ordered
            .Select(summary => new ChampionTierCalculator.TierInput(summary.WinRate, summary.PickRate))
            .ToList();
        var assigned = ChampionTierCalculator.Assign(inputs);

        var maxPickRate = inputs.Max(input => input.PickRate);

        for (var i = 0; i < ordered.Count; i++)
        {
            var summary = ordered[i];
            yield return new ScoredEntry(
                assigned[i],
                ChampionTierCalculator.ScoreOf(inputs[i], maxPickRate),
                new ChampionTierEntryReadModel
                {
                    ChampionId = summary.ChampionId,
                    Position = summary.Position,
                    Games = summary.Games,
                    WinRate = summary.WinRate,
                    PickRate = summary.PickRate,
                });
        }
    }

    private readonly record struct ScoredEntry(string Tier, double Score, ChampionTierEntryReadModel Entry);
}
