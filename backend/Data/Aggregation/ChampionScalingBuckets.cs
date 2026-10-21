using Microsoft.EntityFrameworkCore;

namespace Data.Aggregation;

/// <summary>
/// The game-length buckets of the scaling panel (#1368): under 20 minutes, then five-minute
/// windows up to 35, then everything longer. Held here rather than in the fold so the edges
/// have one spelling — <c>champion_scaling_stats</c> stores the bucket index, and moving an
/// edge means re-folding, not editing a read.
/// </summary>
public static class ChampionScalingBuckets
{
    /// <summary>Number of buckets; indexes run from 0 (shortest) to <c>Count - 1</c>.</summary>
    public const int Count = 5;

    /// <summary>Upper bound (exclusive), in seconds, of every bucket but the last.</summary>
    private static readonly int[] UpperBoundsSeconds = [1200, 1500, 1800, 2100];

    /// <summary>The bucket a game of <paramref name="gameDurationSeconds"/> falls in.</summary>
    public static int Of(int gameDurationSeconds)
    {
        for (var bucket = 0; bucket < UpperBoundsSeconds.Length; bucket++)
        {
            if (gameDurationSeconds < UpperBoundsSeconds[bucket])
            {
                return bucket;
            }
        }

        return Count - 1;
    }

    /// <summary>
    /// The bucket of each match of a fold batch, by match id. Read here rather than in the
    /// fold so the fold never touches a match's duration itself — whether a match is a game
    /// at all is <see cref="ChampionCohort"/>'s call, not the fold's.
    /// </summary>
    public static async Task<Dictionary<string, int>> LoadAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> matchIds,
        CancellationToken ct)
        => await db.Matches
            .AsNoTracking()
            .Where(match => matchIds.Contains(match.Id))
            .Select(match => new { match.Id, match.GameDurationSeconds })
            .ToDictionaryAsync(match => match.Id, match => Of(match.GameDurationSeconds), StringComparer.Ordinal, ct);
}
