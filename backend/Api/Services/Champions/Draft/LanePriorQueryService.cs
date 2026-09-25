using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Draft;

public interface ILanePriorQueryService
{
    /// <summary>
    /// P(lane | champion) for each requested champion, weighted towards the
    /// current patch.
    /// </summary>
    Task<IReadOnlyDictionary<int, LanePrior>> GetAsync(
        IReadOnlyCollection<int> championIds,
        string? patch,
        CancellationToken ct);
}

/// <summary>
/// Where champions are played, read from the synergy baselines.
///
/// <para>
/// The source is <c>champion_synergy_baseline_stats</c> on the <c>ALLY</c>
/// side — "this champion at this lane on a tracked player's team". It is the
/// only aggregate that covers the <em>whole</em> champion pool: the scope table
/// only holds champions our population actually mains, so a champion nobody
/// mains would have no lane distribution at all — precisely on the rare picks
/// where a guess is most needed.
/// </para>
///
/// <para>
/// Patch weighting decays rather than switches. The current patch should count
/// most, but a freshly released one is empty for hours while the fold drains
/// behind Riot's rate limits, and a guesser reading only the current patch
/// would be blind on patch day — which is exactly the day lanes move.
/// </para>
/// </summary>
public sealed class LanePriorQueryService(TrueMainDbContext db) : ILanePriorQueryService
{
    /// <summary>
    /// How many patches back to read. Four is roughly two months: long enough
    /// to survive an empty current patch, short enough that a champion reworked
    /// into a new lane stops being read as its old one within a patch or two.
    /// </summary>
    private const int PatchesConsidered = 4;

    /// <summary>
    /// Weight multiplier per patch of age. At 0.5 the current patch outweighs
    /// every older one put together once it has comparable volume, while an
    /// empty current patch simply yields to the ones behind it.
    /// </summary>
    private const double PatchDecay = 0.5;

    public async Task<IReadOnlyDictionary<int, LanePrior>> GetAsync(
        IReadOnlyCollection<int> championIds,
        string? patch,
        CancellationToken ct)
    {
        if (championIds.Count == 0)
        {
            return new Dictionary<int, LanePrior>();
        }

        var ids = championIds.Distinct().ToList();

        var rows = await db.ChampionSynergyBaselineStats
            .AsNoTracking()
            .Where(b => b.Side == SynergyBaselineSide.Ally && ids.Contains(b.ChampionId))
            .GroupBy(b => new { b.ChampionId, b.TeamPosition, b.Patch })
            .Select(g => new LaneRow(
                g.Key.ChampionId,
                g.Key.TeamPosition,
                g.Key.Patch,
                g.Sum(b => b.Games)))
            .ToListAsync(ct);

        return BuildPriors(rows, PatchFilter.Normalize(patch), ids);
    }

    internal sealed record LaneRow(int ChampionId, string TeamPosition, string Patch, int Games);

    /// <summary>
    /// Fold the rows into one distribution per champion.
    /// </summary>
    /// <remarks>
    /// Separated from the query so the weighting is unit-testable without a
    /// database — it carries all of this service's judgement.
    /// </remarks>
    internal static IReadOnlyDictionary<int, LanePrior> BuildPriors(
        IReadOnlyCollection<LaneRow> rows,
        string? requestedPatch,
        IReadOnlyCollection<int> championIds)
    {
        var weights = PatchWeights(rows.Select(r => r.Patch), requestedPatch);

        var priors = new Dictionary<int, LanePrior>();
        foreach (var championId in championIds)
        {
            var championRows = rows.Where(r => r.ChampionId == championId).ToList();

            var weighted = championRows
                .GroupBy(r => r.TeamPosition, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(r => r.Games * weights.GetValueOrDefault(r.Patch, 0d)),
                    StringComparer.Ordinal);

            var total = weighted.Values.Sum();
            priors[championId] = new LanePrior
            {
                ChampionId = championId,
                // A champion seen only on patches outside the window weighs
                // zero everywhere; an empty map reads as "unseen", which is the
                // honest answer and makes the solver treat it as uniform.
                ByLane = total <= 0
                    ? new Dictionary<string, double>()
                    : weighted.ToDictionary(e => e.Key, e => e.Value / total, StringComparer.Ordinal),
                Games = championRows.Sum(r => r.Games),
            };
        }

        return priors;
    }

    /// <summary>
    /// Weight per patch: 1 for the most recent, decaying with age.
    /// </summary>
    /// <remarks>
    /// Recency is decided by sorting the patches we actually have, not by
    /// counting back from today: we cannot know which patches Riot shipped, only
    /// which ones we hold data for. When the caller names a patch, anything newer
    /// is excluded — a patch filter has to mean the same thing here as everywhere
    /// else on the site.
    /// </remarks>
    private static Dictionary<string, double> PatchWeights(
        IEnumerable<string> patches,
        string? requestedPatch)
    {
        var ordered = patches
            .Distinct(StringComparer.Ordinal)
            .Where(p => requestedPatch is null
                || string.CompareOrdinal(PatchSortKey(p), PatchSortKey(requestedPatch)) <= 0)
            .OrderByDescending(PatchSortKey, StringComparer.Ordinal)
            .Take(PatchesConsidered)
            .ToList();

        return ordered
            .Select((patch, age) => (patch, weight: Math.Pow(PatchDecay, age)))
            .ToDictionary(e => e.patch, e => e.weight, StringComparer.Ordinal);
    }

    /// <summary>
    /// Zero-pad each component so <c>16.4</c> sorts before <c>16.10</c>.
    /// Ordinal string comparison on the raw patch would read "16.10" as older.
    /// </summary>
    private static string PatchSortKey(string patch)
    {
        var parts = patch.Split('.');
        return string.Join('.', parts.Select(part =>
            int.TryParse(part, out var number) ? number.ToString("D4") : part));
    }
}
