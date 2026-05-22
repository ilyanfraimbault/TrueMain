namespace Data.Entities;

public class RankSnapshot
{
    public Guid Id { get; set; }

    public Guid RiotAccountId { get; set; }

    public RiotAccount? RiotAccount { get; set; }

    public DateTime CapturedAtUtc { get; set; }

    public string Tier { get; set; } = string.Empty;

    public string Division { get; set; } = string.Empty;

    public int LeaguePoints { get; set; }

    public int? Wins { get; set; }

    public int? Losses { get; set; }
}
