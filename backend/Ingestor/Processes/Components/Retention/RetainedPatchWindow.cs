using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.Retention;

/// <summary>
/// The set of patches a platform is still <em>current</em> on: the
/// <c>MatchDataRetention:RetainedPatchCount</c> most recent ones it has played.
///
/// <para>
/// Two processes have to agree on it, which is why it lives here rather than inside
/// either of them. <c>MatchDataRetentionProcess</c> uses it to decide which matches to
/// keep; the champion pattern aggregation uses it to decide which patches it may rebuild
/// aggregates for. When the two disagreed, "a patch is live if a single match of it
/// survives" let a handful of late-arriving old games un-freeze a whole retired patch —
/// its aggregates were then replaced by a rebuild over those few stragglers, and the
/// per-champion rebuild raced its own run-start snapshot into a duplicate-key crash
/// (#466, #1549).
/// </para>
/// </summary>
public static class RetainedPatchWindow
{
    internal sealed record ObservedPatch(string PlatformId, string GameVersion, DateTime LastGameStartTimeUtc);

    /// <summary>
    /// The distinct (platform, game version) pairs of the retained queue, each with the
    /// start time of its most recent match.
    ///
    /// <para>
    /// Grouped server-side on purpose: callers only need the couple of newest patches per
    /// platform, but the table holds hundreds of thousands of matches, and projecting one
    /// row per match pulled the whole retained history into memory on every run. The
    /// <c>PatchVersion</c> normalisation below is not translatable to SQL, but the
    /// <c>GROUP BY (platform_id, game_version)</c> and its <c>max(game_start_time_utc)</c>
    /// are, and they return a few hundred rows instead.
    /// </para>
    ///
    /// <para>
    /// Exposed so a test can assert on the SQL it translates to: the whole point of the
    /// shape is that Postgres does the grouping, and a client-evaluated fallback would
    /// silently read the table again.
    /// </para>
    /// </summary>
    internal static IQueryable<ObservedPatch> ObservedPatchesQuery(TrueMainDbContext db, int queueId)
        => db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .GroupBy(match => new { match.PlatformId, match.GameVersion })
            .Select(group => new ObservedPatch(
                group.Key.PlatformId,
                group.Key.GameVersion,
                group.Max(match => match.GameStartTimeUtc)));

    internal static async Task<Dictionary<string, HashSet<string>>> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        int retainedPatchCount,
        CancellationToken ct)
        => Compute(await ObservedPatchesQuery(db, queueId).ToListAsync(ct), retainedPatchCount);

    /// <summary>
    /// Ranks each platform's observed patches by the recency of their newest game and keeps
    /// the <paramref name="retainedPatchCount"/> most recent. Ordering by that maximum —
    /// rather than by the patch version — is what makes a straggler harmless: an old game
    /// ingested today carries an old <c>GameStartTimeUtc</c>, so its patch cannot re-enter
    /// the window just by being re-observed.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> Compute(
        IReadOnlyCollection<ObservedPatch> observedPatches,
        int retainedPatchCount)
        => observedPatches
            .GroupBy(observed => observed.PlatformId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    // Newest patch first, with the game version as a tie-breaker so two
                    // versions sharing a last-seen timestamp keep a stable order.
                    .OrderByDescending(observed => observed.LastGameStartTimeUtc)
                    .ThenByDescending(observed => observed.GameVersion, StringComparer.Ordinal)
                    .Select(observed => PatchVersion.TryParse(observed.GameVersion, out var patch)
                        ? patch.ToMajorMinor()
                        : null)
                    .Where(patch => !string.IsNullOrWhiteSpace(patch))
                    .Select(patch => patch!)
                    .Distinct(StringComparer.Ordinal)
                    .Take(retainedPatchCount)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

    /// <summary>
    /// Flattens the window into the (patch, platform) pairs the aggregation keys on.
    /// </summary>
    internal static HashSet<(string GameVersion, string PlatformId)> ToPatchPlatformPairs(
        IReadOnlyDictionary<string, HashSet<string>> retainedPatchesByPlatform)
        => retainedPatchesByPlatform
            .SelectMany(entry => entry.Value.Select(patch => (GameVersion: patch, PlatformId: entry.Key)))
            .ToHashSet();
}
