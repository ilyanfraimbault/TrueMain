namespace Data.Entities;

public class ChampionPatternAggregate
{
    public Guid Id { get; set; }
    public Guid RiotAccountId { get; set; }
    public RiotAccount RiotAccount { get; set; } = null!;
    public int ChampionId { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public int QueueId { get; set; }
    public string Position { get; set; } = string.Empty;
    public int PrimaryStyleId { get; set; }
    public int SubStyleId { get; set; }
    public int PerksOffense { get; set; }
    public int PerksFlex { get; set; }
    public int PerksDefense { get; set; }
    public int SummonerSpell1Id { get; set; }
    public int SummonerSpell2Id { get; set; }
    public string SkillOrderKey { get; set; } = string.Empty;
    public List<int> StarterItems { get; set; } = [];
    public string StarterItemsKey { get; set; } = string.Empty;
    public int BootsItemId { get; set; }
    public int BuildItem0 { get; set; }
    public int BuildItem1 { get; set; }
    public int BuildItem2 { get; set; }
    public int BuildItem3 { get; set; }
    public int BuildItem4 { get; set; }
    public int BuildItem5 { get; set; }
    public int BuildItem6 { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public DateTime LastGameStartTimeUtc { get; set; }
    public DateTime AggregatedAtUtc { get; set; }
}
