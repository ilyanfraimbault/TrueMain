using Microsoft.EntityFrameworkCore;

namespace Data.Aggregation;

/// <summary>
/// Sends matches back to the champion pattern aggregation after one of their participant
/// rows changed (#1367). The aggregation rebuilds only the slices its pending matches
/// touch, so a writer that rewrites a column the aggregation reads — the rank stamp, the
/// lane, the role-bound slot — has to say so, or the slice keeps the value it was built
/// from until the patch freezes.
///
/// <para>
/// Every caller runs on the aggregate lane, before the pattern step, so the flag it
/// clears cannot be set back by a pattern run that read the old row: the two are
/// sequential, never concurrent.
/// </para>
/// </summary>
public static class PatternAggregationInvalidation
{
    /// <summary>
    /// Clears <c>matches.PatternAggregated</c> on the matches of
    /// <paramref name="participantIds"/>. Returns how many matches were sent back.
    /// </summary>
    public static Task<int> InvalidateMatchesOfParticipantsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<Guid> participantIds,
        CancellationToken ct)
    {
        if (participantIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        return db.Matches
            .Where(match => match.PatternAggregated
                && db.MatchParticipants.Any(participant =>
                    participantIds.Contains(participant.Id) && participant.MatchId == match.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(match => match.PatternAggregated, false), ct);
    }
}
