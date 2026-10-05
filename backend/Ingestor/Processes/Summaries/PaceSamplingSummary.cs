namespace Ingestor.Processes.Summaries;

/// <summary>
/// One run of the low-tier pace sampler (#1912). <see cref="RiotCalls"/> is every call made,
/// failed ones included — the figure the daily ceiling is charged against — and
/// <see cref="Tiers"/> says how many games each tier gained, which is what decides whether the
/// caps are worth raising.
/// </summary>
public sealed record PaceSamplingSummary(
    int RiotCalls,
    int LadderPages,
    int MatchIdLists,
    int MatchesSampled,
    int MatchesAlreadyKnown,
    int MatchesUnusable,
    int FailedCalls,
    int LedgerPruned,
    IReadOnlyList<PaceSamplingTierSummary> Tiers) : IProcessRunSummary;

/// <summary>Games folded for one tier in one run, and the laner-minutes they added.</summary>
public sealed record PaceSamplingTierSummary(string Tier, int Matches, int LanerMinutes);
