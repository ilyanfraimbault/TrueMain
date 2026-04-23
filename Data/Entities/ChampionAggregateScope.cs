namespace Data.Entities;

/// <summary>
/// Master row of the normalised aggregate schema: one row per
/// (riot_account_id, champion_id, game_version, platform_id, queue_id, position)
/// slice, carrying the aggregate totals for the slice. The dimension
/// tables (<see cref="ChampionAggregateSpellPair"/> etc.) reference this
/// row via <c>ScopeId</c> and decompose the old 23-column pattern.
/// </summary>
public class ChampionAggregateScope
{
    public Guid Id { get; set; }

    public Guid RiotAccountId { get; set; }
    public RiotAccount RiotAccount { get; set; } = null!;
    public int ChampionId { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public int QueueId { get; set; }
    public string Position { get; set; } = string.Empty;

    public int Games { get; set; }
    public int Wins { get; set; }
    public DateTime LastGameStartTimeUtc { get; set; }
    public DateTime AggregatedAtUtc { get; set; }

    public List<ChampionAggregateSpellPair> SpellPairs { get; set; } = [];
    public List<ChampionAggregateSkillOrder> SkillOrders { get; set; } = [];
    public List<ChampionAggregateStarterItems> StarterItems { get; set; } = [];
    public List<ChampionAggregateBuild> Builds { get; set; } = [];
    public List<ChampionAggregateRunePage> RunePages { get; set; } = [];
}
