namespace Data.Entities;

/// <summary>
/// Where a <see cref="MainCandidate"/> came from. Ladder candidates are
/// mastery-gated ladder crawl results (rank/points populated); ManualSeed are
/// operator-added accounts; Harvest are derived from orphan
/// <c>match_participants</c> rows (observed games/wins, no mastery data). The
/// source selects how <c>ScoringProcess</c> scores the candidate.
/// </summary>
public enum MainCandidateSource
{
    Ladder = 0,
    ManualSeed = 1,
    Harvest = 2
}
