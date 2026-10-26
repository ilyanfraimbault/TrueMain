namespace TrueMain.ReadModels.Ops;

/// <summary>
/// High-level corpus counters for the admin dashboard landing panel. Nothing here is scoped
/// to a single queue or patch (use <see cref="ChampionStatRow"/> for filtered slices).
///
/// <para>
/// <b>Exact and estimated figures (#2301).</b> The match-side figures —
/// <see cref="TotalMatches"/>, <see cref="TotalParticipants"/>,
/// <see cref="MatchesLast7Days"/>, <see cref="MatchesLast30Days"/> — are the planner's
/// estimates, which the admin prints with <c>≈</c>: exact counts are full scans of the two
/// largest tables and ran the endpoint into the API's statement timeout. They are null, with
/// <see cref="EstimatesUnknownReason"/>, when the estimates could not be read. Every other
/// figure is exact; <see cref="DistinctChampionsWithGames"/> is exact as of the ingestor's
/// last <c>champion_game_counts</c> recount.
/// </para>
/// </summary>
public sealed record OverviewReadModel
{
    public int TrackedAccounts { get; init; }

    /// <summary>Planner estimate of the stored matches (<c>pg_stat_user_tables.n_live_tup</c>).</summary>
    public long? TotalMatches { get; init; }

    /// <summary>Planner estimate of the stored participant rows (<c>n_live_tup</c>).</summary>
    public long? TotalParticipants { get; init; }

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

    /// <summary>Planner estimate of the matches played in the last 7 days.</summary>
    public long? MatchesLast7Days { get; init; }

    /// <summary>Planner estimate of the matches played in the last 30 days.</summary>
    public long? MatchesLast30Days { get; init; }

    /// <summary>Why the estimated figures are null, when they are.</summary>
    public string? EstimatesUnknownReason { get; init; }

    /// <summary>When this payload was computed — it is served from a short cache.</summary>
    public DateTime ComputedAtUtc { get; init; }
}
