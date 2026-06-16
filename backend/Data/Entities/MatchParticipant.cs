namespace Data.Entities;

public class MatchParticipant
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public Match? Match { get; set; }

    public int ParticipantId { get; set; }

    public string Puuid { get; set; } = string.Empty;

    public Guid? RiotAccountId { get; set; }

    public RiotAccount? RiotAccount { get; set; }

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

    public int TotalDamageDealtToChampions { get; set; }

    public int VisionScore { get; set; }

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

    public List<ItemEvent> ItemEvents { get; set; } = new();

    public List<SkillEvent> SkillEvents { get; set; } = new();
}

public class ItemEvent
{
    public int TimestampMs { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public int? BeforeId { get; set; }
    public int? AfterId { get; set; }
}

public class SkillEvent
{
    public int TimestampMs { get; set; }
    public int SkillSlot { get; set; }
    public string LevelUpType { get; set; } = string.Empty;
}
