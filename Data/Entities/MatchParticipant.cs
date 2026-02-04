namespace Data.Entities;

public class MatchParticipant
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public string Puuid { get; set; } = string.Empty;

    public string SummonerName { get; set; } = string.Empty;

    public int SummonerLevel { get; set; }

    public int ChampionId { get; set; }

    public int TeamId { get; set; }

    public string TeamPosition { get; set; } = string.Empty;

    public string IndividualPosition { get; set; } = string.Empty;

    public string Lane { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool Win { get; set; }

    public int Kills { get; set; }

    public int Deaths { get; set; }

    public int Assists { get; set; }

    public int GoldEarned { get; set; }

    public int TotalMinionsKilled { get; set; }

    public int NeutralMinionsKilled { get; set; }

    public int ChampLevel { get; set; }

    public int Item0 { get; set; }

    public int Item1 { get; set; }

    public int Item2 { get; set; }

    public int Item3 { get; set; }

    public int Item4 { get; set; }

    public int Item5 { get; set; }

    public int Item6 { get; set; }

    public int TrinketItemId { get; set; }

    public int PerksDefense { get; set; }

    public int PerksFlex { get; set; }

    public int PerksOffense { get; set; }

    public int PrimaryStyleId { get; set; }

    public int SubStyleId { get; set; }

    public int Summoner1Id { get; set; }

    public int Summoner2Id { get; set; }

    public ICollection<ParticipantItemEvent> ItemEvents { get; set; } = new List<ParticipantItemEvent>();

    public ICollection<ParticipantSkillEvent> SkillEvents { get; set; } = new List<ParticipantSkillEvent>();
}
