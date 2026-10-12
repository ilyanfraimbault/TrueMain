using Core.Lol.Ranking;

namespace Data.Entities;

public class RankSnapshot
{
    public Guid Id { get; set; }

    public Guid RiotAccountId { get; set; }

    public RiotAccount? RiotAccount { get; set; }

    public DateTime CapturedAtUtc { get; set; }

    public RankTier Tier { get; set; }

    public RankDivision Division { get; set; }

    public int LeaguePoints { get; set; }

    public int? Wins { get; set; }

    public int? Losses { get; set; }
}
