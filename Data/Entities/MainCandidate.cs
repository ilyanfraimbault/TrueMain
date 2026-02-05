namespace Data.Entities;

public class MainCandidate
{
    public Guid Id { get; set; }

    public string PlatformId { get; set; } = string.Empty;

    public string Puuid { get; set; } = string.Empty;

    public int ChampionId { get; set; }

    public int ChampionRankInMasteryTop { get; set; }

    public long ChampionPoints { get; set; }

    public DateTime LastPlayTimeUtc { get; set; }

    public DateTime DiscoveredAtUtc { get; set; }

    public double Score { get; set; }

    public MainCandidateStatus Status { get; set; } = MainCandidateStatus.New;

    public DateTime? ScoredAtUtc { get; set; }
}
