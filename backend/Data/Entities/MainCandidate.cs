namespace Data.Entities;

public class MainCandidate
{
    public Guid Id { get; set; }

    // The (platform, puuid, champion) identity of a candidate never changes after
    // it is created — only its scoring/lifecycle fields do — so it is required + init.
    public required string PlatformId { get; init; }

    public required string Puuid { get; init; }

    public required int ChampionId { get; init; }

    public int ChampionRankInMasteryTop { get; set; }

    public long ChampionPoints { get; set; }

    /// <summary>
    /// Origin of the candidate. Defaults to <see cref="MainCandidateSource.Ladder"/>
    /// so rows that predate participant harvesting keep their mastery-crawl semantics.
    /// </summary>
    public MainCandidateSource Source { get; set; } = MainCandidateSource.Ladder;

    /// <summary>
    /// Games observed for this (puuid, champion) in <c>match_participants</c> when the
    /// candidate is harvested (<see cref="MainCandidateSource.Harvest"/>). Zero for
    /// ladder/manual candidates. This is a biased prior (we only see a player's games
    /// when they shared a lobby with a tracked account), not a main verdict.
    /// </summary>
    public int ObservedGames { get; set; }

    /// <summary>
    /// Wins within <see cref="ObservedGames"/>; together they give the observed winrate.
    /// </summary>
    public int ObservedWins { get; set; }

    /// <summary>
    /// Mastery last-play time for ladder/manual candidates, or the most-recent observed
    /// game time for harvested candidates. Drives the scoring recency component.
    /// </summary>
    public DateTime LastPlayTimeUtc { get; set; }

    public DateTime DiscoveredAtUtc { get; set; }

    public double Score { get; set; }

    public MainCandidateStatus Status { get; set; } = MainCandidateStatus.New;

    public DateTime? ScoredAtUtc { get; set; }

    public DateTime? ValidatedAtUtc { get; set; }
}
