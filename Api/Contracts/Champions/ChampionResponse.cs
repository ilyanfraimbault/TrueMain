namespace TrueMain.Contracts.Champions;

public sealed class ChampionResponse
{
    public ChampionSummaryResponse Summary { get; init; } = new();

    public ChampionCoreResponse Core { get; init; } = new();

    public ChampionAdvancedDetailsResponse Advanced { get; init; } = new();

    public ChampionBuildTreeResponse BuildTree { get; init; } = new();
}

public sealed class ChampionSummaryResponse
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public double WinRate { get; init; }

    public int TrueMainCount { get; init; }

    public string Position { get; init; } = string.Empty;

    public string LatestPatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }
}

public sealed class ChampionCoreResponse
{
    public int SampleSize { get; init; }

    public ItemSetOptionResponse? StarterItems { get; init; }

    public ItemSetOptionResponse? Boots { get; init; }

    public BuildPathPreviewResponse? BuildPath { get; init; }

    public SummonerSpellOptionResponse? SummonerSpells { get; init; }

    public SkillOrderOptionResponse? SkillOrder { get; init; }
}

public sealed class ChampionAdvancedDetailsResponse
{
    public IReadOnlyList<ItemSetOptionResponse> StarterItemOptions { get; init; } = [];

    public IReadOnlyList<SummonerSpellOptionResponse> SummonerSpellOptions { get; init; } = [];

    public IReadOnlyList<SkillOrderOptionResponse> SkillOrderOptions { get; init; } = [];
}

public sealed class SummonerSpellOptionResponse
{
    public int Spell1Id { get; init; }

    public int Spell2Id { get; init; }

    public int Games { get; init; }

    public double PlayRate { get; init; }

    public double WinRate { get; init; }
}

public sealed class SkillOrderOptionResponse
{
    public IReadOnlyList<string> Sequence { get; init; } = [];

    public int Games { get; init; }

    public double PlayRate { get; init; }

    public double WinRate { get; init; }
}

public sealed class ItemSetOptionResponse
{
    public IReadOnlyList<int> ItemIds { get; init; } = [];

    public int Games { get; init; }

    public double PlayRate { get; init; }

    public double WinRate { get; init; }
}

public sealed class BuildPathPreviewResponse
{
    public IReadOnlyList<int> ItemIds { get; init; } = [];
}
