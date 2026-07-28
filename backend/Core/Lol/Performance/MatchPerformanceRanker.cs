namespace Core.Lol.Performance;

/// <summary>One participant's score plus the tiebreakers used to order the match.</summary>
public sealed record MatchPerformanceEntry
{
    public int ParticipantId { get; init; }

    public bool Win { get; init; }

    /// <summary>The 0–100 score from <see cref="PerformanceScore.Compute"/>.</summary>
    public int Score { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Assists { get; init; }
}

/// <summary>Where one participant landed in the match's performance ranking.</summary>
public sealed record MatchPerformancePlacement
{
    public int ParticipantId { get; init; }

    /// <summary>The 0–100 score this placement was derived from, carried through so a caller that only keeps the ranking still has the number behind it.</summary>
    public int Score { get; init; }

    /// <summary>1-based rank of this participant's score within the match (1 = best of all 10).</summary>
    public int Placement { get; init; }

    /// <summary>True for the single best-scoring participant on the winning side.</summary>
    public bool IsMvp { get; init; }

    /// <summary>True for the single best-scoring participant on the losing side.</summary>
    public bool IsAce { get; init; }
}

/// <summary>
/// Turns the per-participant <see cref="PerformanceScore"/> values of one match into a
/// strict 1..N placement plus the MVP / ACE accolades. Pure and deterministic: the
/// ordering never depends on the order the entries arrive in.
///
/// <para>Participants are ordered by score descending, then by takedowns
/// (<c>kills + assists</c>) descending, then by deaths ascending, then by participant id
/// ascending. The last key guarantees a total order, so equal scores still produce
/// distinct, stable placements instead of an arbitrary shuffle between requests.</para>
///
/// <para><b>MVP</b> is the best-placed participant among the winners, <b>ACE</b> the
/// best-placed among the losers — so exactly one of each exists in a normal 5v5, and
/// neither exists for a side with no participants.</para>
/// </summary>
public static class MatchPerformanceRanker
{
    /// <summary>
    /// Ranks every entry of a match, keyed by participant id.
    /// </summary>
    public static IReadOnlyDictionary<int, MatchPerformancePlacement> Rank(
        IEnumerable<MatchPerformanceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.Kills + e.Assists)
            .ThenBy(e => e.Deaths)
            .ThenBy(e => e.ParticipantId)
            .ToList();

        // The ordering is already the ranking, so the first winner / first loser
        // encountered while walking it are the MVP and the ACE.
        var mvpParticipantId = ordered.FirstOrDefault(e => e.Win)?.ParticipantId;
        var aceParticipantId = ordered.FirstOrDefault(e => !e.Win)?.ParticipantId;

        var placements = new Dictionary<int, MatchPerformancePlacement>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];
            placements[entry.ParticipantId] = new MatchPerformancePlacement
            {
                ParticipantId = entry.ParticipantId,
                Score = entry.Score,
                Placement = i + 1,
                IsMvp = entry.ParticipantId == mvpParticipantId,
                IsAce = entry.ParticipantId == aceParticipantId,
            };
        }

        return placements;
    }
}
