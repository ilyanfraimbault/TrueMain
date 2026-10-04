namespace TrueMain.ReadModels.Ops;

/// <summary>
/// One <c>main_candidates</c> row: where it sits in the
/// New→Scored→Queued→Processing→Validated funnel, and what the scorer saw.
/// </summary>
public sealed record AccountExplorerCandidateReadModel
{
    public Guid Id { get; init; }

    public int ChampionId { get; init; }

    /// <summary>The <c>MainCandidateStatus</c> name.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// The <c>MainCandidateSource</c> name. Note that <c>ManualSeed</c> is never
    /// assigned in production — <c>ManualSeedProcess</c> reuses the ladder
    /// upsert, so a manually seeded candidate reads <c>Ladder</c>. Read
    /// <see cref="AccountExplorerReadModel.SeedRequest"/> for the manual trail.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>The 0–100 blend computed by <c>ScoringProcess</c>.</summary>
    public double Score { get; init; }

    /// <summary>
    /// The persisted inputs the score was computed from.
    /// <strong>The score's components are not stored</strong> — only the final
    /// blend is — so they cannot be shown. Recomputing them here would mix
    /// today's champion-scarcity snapshot into a number produced against an older
    /// one, and would silently disagree with <see cref="Score"/>.
    /// </summary>
    public AccountExplorerCandidateScoreInputsReadModel ScoreInputs { get; init; } = new();

    public DateTime DiscoveredAtUtc { get; init; }

    public DateTime? ScoredAtUtc { get; init; }

    public DateTime? ValidatedAtUtc { get; init; }
}

/// <summary>
/// What <c>ScoringProcess</c> had to work with. Ladder candidates carry mastery
/// rank/points; harvest candidates carry observed games/wins instead and leave
/// the mastery fields at zero.
/// </summary>
public sealed record AccountExplorerCandidateScoreInputsReadModel
{
    /// <summary>Mastery <c>lastPlayTime</c> (ladder) or last observed game (harvest) — the recency input.</summary>
    public DateTime LastPlayTimeUtc { get; init; }

    /// <summary>Rank of this champion in the account's mastery top-N; 0 for harvest candidates.</summary>
    public int ChampionRankInMasteryTop { get; init; }

    /// <summary>Mastery points; 0 for harvest candidates.</summary>
    public long ChampionPoints { get; init; }

    /// <summary>Games observed in orphan participant rows; 0 for ladder candidates.</summary>
    public int ObservedGames { get; init; }

    /// <summary>Wins among <see cref="ObservedGames"/>. Persisted but not a scoring input yet.</summary>
    public int ObservedWins { get; init; }
}
