using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions;

/// <summary>
/// Reads the ban aggregates (#920) for a set of patches and folds them into a
/// per-patch, per-champion ban rate. Shared by the champion directory / tier list
/// and the champion trend chart so the two can never disagree on the same number.
/// </summary>
internal static class ChampionBanRateQueries
{
    /// <summary>
    /// Loads one <see cref="BanRateScope"/> per patch that has ban data, from
    /// <c>champion_ban_stats</c> and the <c>ban_scope_totals</c> denominators.
    ///
    /// <para>
    /// <c>bracketBands</c> is the resolved elo filter, or <see langword="null"/> for
    /// no filter. Null reads the stored <see cref="EloBracket.All"/> row rather than
    /// summing the bands: a match is folded into every band its players sat in, so
    /// the bands overlap and their sum is not the match count. A multi-band filter
    /// (e.g. <c>GOLD_PLUS</c>) does sum, on both sides of the ratio, which weights a
    /// match by how many of the selected bands it touched.
    /// </para>
    ///
    /// <para>
    /// A patch absent from the returned dictionary has no ban data at all — the read
    /// must surface that as unknown, not as a zero rate. Bans only exist for matches
    /// ingested since #920 shipped, so older patches are legitimately missing and
    /// every consumer has to handle the gap.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, BanRateScope>> LoadAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> patches,
        IReadOnlyList<string>? bracketBands,
        CancellationToken ct)
    {
        if (patches.Count == 0)
        {
            return new Dictionary<string, BanRateScope>(StringComparer.Ordinal);
        }

        var bands = bracketBands is { Count: > 0 }
            ? bracketBands
            : [EloBracket.All];

        var totals = await db.BanScopeTotals
            .AsNoTracking()
            .Where(total => patches.Contains(total.Patch) && bands.Contains(total.EloBracket))
            .GroupBy(total => total.Patch)
            .Select(group => new { Patch = group.Key, Matches = group.Sum(total => (long)total.Matches) })
            .ToListAsync(ct);

        // A scope with no matches folded carries no information — drop it here so a
        // consumer never divides by zero and never reads a 0/0 as "never banned".
        var matchesByPatch = totals
            .Where(entry => entry.Matches > 0)
            .ToDictionary(entry => entry.Patch, entry => entry.Matches, StringComparer.Ordinal);

        if (matchesByPatch.Count == 0)
        {
            return new Dictionary<string, BanRateScope>(StringComparer.Ordinal);
        }

        var countedPatches = matchesByPatch.Keys.ToList();
        var banRows = await db.ChampionBanStats
            .AsNoTracking()
            .Where(stat => countedPatches.Contains(stat.Patch) && bands.Contains(stat.EloBracket))
            .GroupBy(stat => new { stat.Patch, stat.ChampionId })
            .Select(group => new
            {
                group.Key.Patch,
                group.Key.ChampionId,
                Bans = group.Sum(stat => (long)stat.Bans),
            })
            .ToListAsync(ct);

        var bansByPatch = banRows
            .GroupBy(row => row.Patch, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<int, long>)group.ToDictionary(row => row.ChampionId, row => row.Bans),
                StringComparer.Ordinal);

        return matchesByPatch.ToDictionary(
            entry => entry.Key,
            entry => new BanRateScope(
                entry.Value,
                bansByPatch.GetValueOrDefault(entry.Key) ?? new Dictionary<int, long>()),
            StringComparer.Ordinal);
    }
}

/// <summary>
/// The two halves of a ban rate for one patch and elo filter: how many matches were
/// folded, and how many of them banned each champion.
/// </summary>
internal sealed record BanRateScope(long Matches, IReadOnlyDictionary<int, long> BansByChampion)
{
    /// <summary>
    /// Share of the scope's matches that banned <paramref name="championId"/>. A
    /// champion with no row was seen and never banned, so this is <c>0</c> rather
    /// than unknown — the unknown case is the whole scope being absent.
    /// </summary>
    public double RateFor(int championId)
        => RateMath.Rate(BansByChampion.GetValueOrDefault(championId), Matches);
}
