namespace TrueMain.ReadModels.Ops;

/// <summary>
/// High-level corpus counters for the admin dashboard landing panel. Every value
/// is a straight aggregate over the persisted entities; nothing here is scoped to
/// a single queue or patch (use <see cref="ChampionStatRow"/> for filtered slices).
/// </summary>
public sealed record OverviewReadModel
{
    public int TrackedAccounts { get; init; }

    public long TotalMatches { get; init; }

    public long TotalParticipants { get; init; }

    /// <summary>
    /// Main-candidate counts keyed by their <c>MainCandidateStatus</c> name
    /// (e.g. "New", "Scored", "Validated"). Every defined status is present, so
    /// the map is stable for the frontend even when a status has zero rows.
    /// </summary>
    public IReadOnlyDictionary<string, int> CandidatesByStatus { get; init; } =
        new Dictionary<string, int>();

    public int TotalMains { get; init; }

    public int TotalOtps { get; init; }

    public int DistinctChampionsWithGames { get; init; }

    public int DistinctChampionsWithMains { get; init; }

    public long MatchesLast7Days { get; init; }

    public long MatchesLast30Days { get; init; }
}
