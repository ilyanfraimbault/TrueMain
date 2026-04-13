namespace TrueMain.ReadModels.Champions;

public sealed class ChampionFoundationReadModel
{
    public ChampionSummaryReadModel Summary { get; init; } = new();

    public ChampionAdvancedDetailsReadModel Advanced { get; init; } = new();

    public IReadOnlyList<ChampionCorrelatedPatternReadModel> CorrelatedPatterns { get; init; } = [];
}

public sealed class ChampionSummaryReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public double WinRate { get; init; }

    public int TrueMainCount { get; init; }

    public string Position { get; init; } = string.Empty;

    public string LatestPatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }
}

public sealed class ChampionAdvancedDetailsReadModel
{
    public int SampleSize { get; init; }

    public IReadOnlyList<ItemSetOptionReadModel> StarterItemOptions { get; init; } = [];

    public IReadOnlyList<SummonerSpellOptionReadModel> SummonerSpellOptions { get; init; } = [];

    public IReadOnlyList<SkillOrderOptionReadModel> SkillOrderOptions { get; init; } = [];
}

public sealed class ChampionCoreReadModel
{
    public int SampleSize { get; init; }

    public ItemSetOptionReadModel? StarterItems { get; init; }

    public IReadOnlyList<int> BuildPathItemIds { get; init; } = [];

    public SummonerSpellOptionReadModel? SummonerSpells { get; init; }

    public SkillOrderOptionReadModel? SkillOrder { get; init; }
}

public sealed class ChampionCorrelatedPatternReadModel
{
    public ItemSetOptionReadModel? StarterItems { get; init; }

    public IReadOnlyList<int> BuildItemIds { get; init; } = [];

    public SummonerSpellOptionReadModel SummonerSpells { get; init; } = new();

    public SkillOrderOptionReadModel SkillOrder { get; init; } = new();

    public int Games { get; init; }

    public int Wins { get; init; }

    public DateTime LastUpdatedAtUtc { get; init; }
}

public sealed class SummonerSpellOptionReadModel
{
    public int Spell1Id { get; init; }

    public int Spell2Id { get; init; }

    public int Games { get; init; }

    public double PlayRate { get; init; }

    public double WinRate { get; init; }
}

public sealed class SkillOrderOptionReadModel
{
    public IReadOnlyList<string> Sequence { get; init; } = [];

    public int Games { get; init; }

    public double PlayRate { get; init; }

    public double WinRate { get; init; }
}

public sealed class ItemSetOptionReadModel
{
    public IReadOnlyList<int> ItemIds { get; init; } = [];

    public int Games { get; init; }

    public double PlayRate { get; init; }

    public double WinRate { get; init; }
}
