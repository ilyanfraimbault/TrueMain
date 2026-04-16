using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

internal sealed class ChampionPatternAggregationInputs
{
    public required List<AggregateScopeKey> ExistingAggregateScopes { get; init; }
    public required List<AggregateSourceRow> SourceRows { get; init; }
}

internal sealed class ChampionPatternAggregationResult
{
    public required List<ChampionPatternAggregate> AggregateRows { get; init; }
    public required int SourceRowCount { get; init; }
}

internal sealed class AggregateSourceRow
{
    public string MatchId { get; init; } = string.Empty;
    public int ChampionId { get; init; }
    public string GameVersion { get; init; } = string.Empty;
    public string PlatformId { get; init; } = string.Empty;
    public int QueueId { get; init; }
    public DateTime GameStartTimeUtc { get; init; }
    public int GameDurationSeconds { get; init; }
    public Guid RiotAccountId { get; init; }
    public bool Win { get; init; }
    public string? Position { get; init; }
    public int Summoner1Id { get; init; }
    public int Summoner2Id { get; init; }
    public int PrimaryStyleId { get; init; }
    public int SubStyleId { get; init; }
    public int PerksOffense { get; init; }
    public int PerksFlex { get; init; }
    public int PerksDefense { get; init; }
    public List<ItemEvent> ItemEvents { get; init; } = [];
    public List<SkillEvent> SkillEvents { get; init; } = [];
    public int Item0 { get; init; }
    public int Item1 { get; init; }
    public int Item2 { get; init; }
    public int Item3 { get; init; }
    public int Item4 { get; init; }
    public int Item5 { get; init; }
    public int Item6 { get; init; }
}

internal sealed record ExpandedSourceRow(
    Guid RiotAccountId,
    int ChampionId,
    string GameVersion,
    string PlatformId,
    int QueueId,
    string Position,
    int PrimaryStyleId,
    int SubStyleId,
    int PerksOffense,
    int PerksFlex,
    int PerksDefense,
    int SummonerSpell1Id,
    int SummonerSpell2Id,
    string SkillOrderKey,
    List<int> StarterItems,
    int BootsItemId,
    int BuildItem0,
    int BuildItem1,
    int BuildItem2,
    int BuildItem3,
    int BuildItem4,
    int BuildItem5,
    int BuildItem6,
    bool Win,
    DateTime GameStartTimeUtc)
{
    public string StarterItemsKey { get; } = string.Join("-", StarterItems);
}

internal sealed record AggregateKey(
    Guid RiotAccountId,
    int ChampionId,
    string GameVersion,
    string PlatformId,
    int QueueId,
    string Position,
    int PrimaryStyleId,
    int SubStyleId,
    int PerksOffense,
    int PerksFlex,
    int PerksDefense,
    int SummonerSpell1Id,
    int SummonerSpell2Id,
    string SkillOrderKey,
    string StarterItemsKey,
    int BootsItemId,
    int BuildItem0,
    int BuildItem1,
    int BuildItem2,
    int BuildItem3,
    int BuildItem4,
    int BuildItem5,
    int BuildItem6);

internal sealed record AggregateScopeKey(
    int ChampionId,
    string GameVersion,
    string PlatformId,
    int QueueId);
