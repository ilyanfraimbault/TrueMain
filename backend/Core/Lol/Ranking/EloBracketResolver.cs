namespace Core.Lol.Ranking;

/// <summary>
/// Resolves the per-game <see cref="EloBracket"/> band for a tracked account by
/// picking the <c>rank_snapshots</c> capture nearest (in absolute time) to a
/// match's start and mapping its tier to a band. Ties break toward the earlier
/// capture so the same game always buckets the same way across re-runs.
///
/// Shared by every writer that stamps a per-game band — the champion pattern
/// aggregation source reader and the <c>match_participants</c> enrichment pass —
/// so both derive brackets identically. The nearest-capture reduction is a plain
/// in-memory fold (a correlated "nearest" subquery does not translate to
/// efficient SQL), so callers load the candidate snapshots first.
/// </summary>
public static class EloBracketResolver
{
    /// <summary>
    /// Returns the band for the snapshot nearest to <paramref name="gameStartUtc"/>,
    /// or <see cref="EloBracket.Unranked"/> when there are no snapshots.
    /// </summary>
    public static string FromNearestSnapshot(
        IReadOnlyCollection<(DateTime CapturedAtUtc, string? Tier)> snapshots,
        DateTime gameStartUtc)
    {
        if (snapshots.Count == 0)
        {
            return EloBracket.Unranked;
        }

        var nearest = snapshots
            .OrderBy(snapshot => Math.Abs((snapshot.CapturedAtUtc - gameStartUtc).Ticks))
            .ThenBy(snapshot => snapshot.CapturedAtUtc)
            .First();

        return EloBracket.FromTier(nearest.Tier);
    }
}
