using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

internal sealed class ChampionPatternAggregationInputs
{
    public required List<AggregateScopeKey> ExistingAggregateScopes { get; init; }
    public required List<AggregateSourceRow> SourceRows { get; init; }
}

internal sealed class ChampionPatternAggregationResult
{
    /// <summary>
    /// Scope master rows. Pattern intents reference each scope's
    /// <see cref="ChampionAggregateScope.Id"/> so the persister can attach
    /// FKs after dim resolution.
    /// </summary>
    public required List<ChampionAggregateScope> Scopes { get; init; }

    /// <summary>
    /// Phase 6 pattern intents — one per (scope, full combo) tuple.
    /// </summary>
    public required List<PatternIntent> Patterns { get; init; }

    public required int SourceRowCount { get; init; }
}

internal sealed class AggregateSourceRow
{
    public string MatchId { get; init; } = string.Empty;
    public int ParticipantId { get; init; }
    public int ChampionId { get; init; }
    public string GameVersion { get; init; } = string.Empty;
    public string PlatformId { get; init; } = string.Empty;
    public int QueueId { get; init; }
    public DateTime GameStartTimeUtc { get; init; }
    public int GameDurationSeconds { get; init; }
    public Guid RiotAccountId { get; init; }
    public bool Win { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public string? Position { get; init; }
    public int Summoner1Id { get; init; }
    public int Summoner2Id { get; init; }
    public int PrimaryStyleId { get; init; }
    public int SubStyleId { get; init; }
    public int PerksOffense { get; init; }
    public int PerksFlex { get; init; }
    public int PerksDefense { get; init; }

    // Elo bucket of the player at game time, resolved after the initial LINQ
    // projection by HydrateEloBracketsAsync (nearest rank_snapshots capture to
    // GameStartTimeUtc). Defaults to UNRANKED when no snapshot is found.
    public string EloBracket { get; set; } = Core.Lol.Ranking.EloBracket.Unranked;

    // Populated after the initial LINQ projection by HydratePerkSelectionsAsync
    // (the six individual perk ids live in participant_perk_selections ⋈
    // perk_selection_catalog, not on match_participants directly).
    public int PrimaryKeystoneId { get; set; }
    public int PrimaryPerk1Id { get; set; }
    public int PrimaryPerk2Id { get; set; }
    public int PrimaryPerk3Id { get; set; }
    public int SecondaryPerk1Id { get; set; }
    public int SecondaryPerk2Id { get; set; }

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
    string EloBracket,
    int PrimaryStyleId,
    int SubStyleId,
    int PerksOffense,
    int PerksFlex,
    int PerksDefense,
    int PrimaryKeystoneId,
    int PrimaryPerk1Id,
    int PrimaryPerk2Id,
    int PrimaryPerk3Id,
    int SecondaryPerk1Id,
    int SecondaryPerk2Id,
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
    int Kills,
    int Deaths,
    int Assists,
    DateTime GameStartTimeUtc)
{
    public string StarterItemsKey { get; } = string.Join("-", StarterItems);
}

internal sealed record AggregateScopeKey(
    int ChampionId,
    string GameVersion,
    string PlatformId,
    int QueueId);
