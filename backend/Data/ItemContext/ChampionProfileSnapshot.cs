using Core.Lol.Patches;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.ItemContext;

/// <summary>
/// The champion profiles (#1449) a fold uses to qualify the drafts of one patch, resolved
/// once and then read per participant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a snapshot instead of "the profile of this patch".</b> Profiles are folded from
/// the same matches this pass is folding, so on the first day of a patch they are nearly
/// empty — and a draft qualified against an empty profile is not qualified at all. The
/// snapshot therefore takes, per champion and position, the most recent profile at or
/// before the patch that clears the games floor, walking back a bounded number of patches.
/// A champion's damage type, sustain, crowd control and range are among the most stable
/// things in the game; last patch's profile is a far better answer than this patch's
/// forty games, and an infinitely better one than nothing.
/// </para>
/// <para>
/// <b>The position fallback.</b> A champion played off-role — Yasuo support, Sett mid —
/// may never clear the floor at that position, and dropping it would cost the axis for the
/// whole team it was in. The classification barely moves with the role, so the snapshot
/// falls back to the champion's best-covered position rather than to nothing, and the
/// evaluator's "at most one unknown per side" rule is what covers the rest.
/// </para>
/// </remarks>
public sealed class ChampionProfileSnapshot
{
    private readonly Dictionary<(int ChampionId, string Position), ChampionProfileFacts> _byPosition;
    private readonly Dictionary<int, ChampionProfileFacts> _byChampion;

    private ChampionProfileSnapshot(
        Dictionary<(int, string), ChampionProfileFacts> byPosition,
        Dictionary<int, ChampionProfileFacts> byChampion)
    {
        _byPosition = byPosition;
        _byChampion = byChampion;
    }

    /// <summary>A snapshot that knows nothing — every axis it is asked about is unavailable.</summary>
    public static ChampionProfileSnapshot Empty { get; } = new([], []);

    /// <summary>How many (champion, position) profiles the snapshot resolved, for logging.</summary>
    public int Count => _byPosition.Count;

    /// <summary>Every <c>(champion, position)</c> profile the snapshot resolved.</summary>
    public IEnumerable<ChampionProfileFacts> ByPosition => _byPosition.Values;

    /// <summary>
    /// Each champion's best-covered profile — what <see cref="Find"/> answers for a position
    /// the champion has no profile at.
    /// </summary>
    public IEnumerable<ChampionProfileFacts> ByChampion => _byChampion.Values;

    /// <summary>
    /// The profile for this champion at this position, its best-covered position as a
    /// fallback, or <see langword="null"/> when the champion has no usable profile at all.
    /// </summary>
    public ChampionProfileFacts? Find(int championId, string position)
        => _byPosition.TryGetValue((championId, position), out var facts)
            ? facts
            : _byChampion.GetValueOrDefault(championId);

    /// <summary>
    /// Resolves the snapshot for <paramref name="patch"/> from the profile aggregate.
    /// </summary>
    /// <param name="db">The context to read the profile aggregate from.</param>
    /// <param name="patch">The patch whose drafts are being qualified.</param>
    /// <param name="lookbackPatches">
    /// How many patches before <paramref name="patch"/> may be used when the patch itself
    /// has no qualifying row. 0 restricts the snapshot to the patch.
    /// </param>
    /// <param name="minGames">Games a profile row must hold to be trusted.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<ChampionProfileSnapshot> LoadAsync(
        TrueMainDbContext db,
        string patch,
        int lookbackPatches,
        int minGames,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!PatchVersion.TryParse(patch, out var target))
        {
            return Empty;
        }

        // The window is chosen from the patches that actually carry profiles, not from
        // arithmetic on version numbers: patches are not contiguous in this database (a
        // patch nobody was tracked through simply has no rows), so counting backwards
        // numerically would look past real data into gaps.
        var patches = await db.ChampionProfileStats
            .AsNoTracking()
            .Where(profile => profile.Games >= minGames)
            .Select(profile => profile.Patch)
            .Distinct()
            .ToListAsync(ct);

        var windowPatches = Window(patches, target, lookbackPatches);
        if (windowPatches.Count == 0)
        {
            return Empty;
        }

        var rows = await db.ChampionProfileStats
            .AsNoTracking()
            .Where(profile => windowPatches.Contains(profile.Patch) && profile.Games >= minGames)
            .ToListAsync(ct);

        return Build(rows, windowPatches);
    }

    /// <summary>
    /// The same resolution over rows already in memory — an offline evaluation replaying
    /// exported profiles must classify a draft exactly as the fold does.
    /// </summary>
    public static ChampionProfileSnapshot FromRows(
        IReadOnlyCollection<ChampionProfileStat> rows,
        string patch,
        int lookbackPatches,
        int minGames)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (!PatchVersion.TryParse(patch, out var target))
        {
            return Empty;
        }

        var qualifying = rows.Where(row => row.Games >= minGames).ToList();
        var windowPatches = Window(qualifying.Select(row => row.Patch).Distinct().ToList(), target, lookbackPatches);
        if (windowPatches.Count == 0)
        {
            return Empty;
        }

        return Build([.. qualifying.Where(row => windowPatches.Contains(row.Patch))], windowPatches);
    }

    /// <summary>The newest qualifying patches at or before the target, newest first.</summary>
    private static List<string> Window(IReadOnlyCollection<string> patches, PatchVersion target, int lookbackPatches)
        => [.. patches
            .Select(raw => PatchVersion.TryParse(raw, out var version) ? (Raw: raw, Version: version) : default)
            .Where(entry => entry.Raw is not null && entry.Version <= target)
            .OrderByDescending(entry => entry.Version)
            .Take(lookbackPatches + 1)
            .Select(entry => entry.Raw)];

    private static ChampionProfileSnapshot Build(IReadOnlyList<ChampionProfileStat> rows, IReadOnlyList<string> windowPatches)
    {
        var rank = windowPatches
            .Select((raw, index) => (Raw: raw, Index: index))
            .ToDictionary(entry => entry.Raw, entry => entry.Index, StringComparer.Ordinal);

        var byPosition = new Dictionary<(int, string), ChampionProfileFacts>();
        var byChampion = new Dictionary<int, ChampionProfileFacts>();
        var championGames = new Dictionary<int, int>();

        // Newest patch first, then most games: the first row to claim a (champion,
        // position) key wins, so that pair takes its most recent qualifying profile. The
        // per-champion fallback is chosen on games instead — across the whole window,
        // because a fallback is only ever a classification and the deepest sample
        // classifies best.
        foreach (var row in rows.OrderBy(row => rank[row.Patch]).ThenByDescending(row => row.Games))
        {
            var facts = ChampionProfileFacts.From(row);
            if (facts is null)
            {
                continue;
            }

            byPosition.TryAdd((row.ChampionId, row.Position), facts);

            if (!championGames.TryGetValue(row.ChampionId, out var best) || row.Games > best)
            {
                championGames[row.ChampionId] = row.Games;
                byChampion[row.ChampionId] = facts;
            }
        }

        return new ChampionProfileSnapshot(byPosition, byChampion);
    }
}
