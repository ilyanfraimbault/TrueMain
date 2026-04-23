using System.Text.Json.Serialization;

namespace TrueMain.ReadModels.Champions;

public sealed class ChampionFoundationReadModel
{
    public ChampionSummaryReadModel Summary { get; init; } = new();

    public ChampionCoreReadModel Core { get; init; } = new();

    public ChampionAdvancedDetailsReadModel Advanced { get; init; } = new();
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
    /// <summary>
    /// Internal scratchpad reused by <c>ChampionCoreBuilder</c>; not part of
    /// the API contract (<c>ChampionCoreReadModel.SampleSize</c> already
    /// surfaces it under <c>core.sampleSize</c>).
    /// </summary>
    [JsonIgnore]
    public int SampleSize { get; init; }

    public IReadOnlyList<ItemSetOptionReadModel> StarterItemOptions { get; init; } = [];

    public IReadOnlyList<SummonerSpellOptionReadModel> SummonerSpellOptions { get; init; } = [];

    public IReadOnlyList<SkillOrderOptionReadModel> SkillOrderOptions { get; init; } = [];

    public IReadOnlyList<RunePageOptionReadModel> RunePageOptions { get; init; } = [];
}

public sealed class ChampionCoreReadModel
{
    public int SampleSize { get; init; }

    public ItemSetOptionReadModel? StarterItems { get; init; }

    public ItemSetOptionReadModel? Boots { get; init; }

    public BuildPathPreviewReadModel? BuildPath { get; init; }

    public SummonerSpellOptionReadModel? SummonerSpells { get; init; }

    public SkillOrderOptionReadModel? SkillOrder { get; init; }

    public RunePageOptionReadModel? RunePage { get; init; }
}

public sealed class RunePageOptionReadModel
{
    /// <summary>
    /// The first completed build item this page is correlated with. 0 means
    /// "unknown" (backfilled rows prior to the first aggregation run, or
    /// participants with no completed build item at game end).
    /// </summary>
    public int FirstItemId { get; init; }

    public int PrimaryStyleId { get; init; }
    public int PrimaryKeystoneId { get; init; }
    public int PrimaryPerk1Id { get; init; }
    public int PrimaryPerk2Id { get; init; }
    public int PrimaryPerk3Id { get; init; }
    public int SecondaryStyleId { get; init; }
    public int SecondaryPerk1Id { get; init; }
    public int SecondaryPerk2Id { get; init; }
    public int StatOffense { get; init; }
    public int StatFlex { get; init; }
    public int StatDefense { get; init; }
    public int Games { get; init; }
    public double PlayRate { get; init; }
    public double WinRate { get; init; }
}

public sealed class BuildPathPreviewReadModel
{
    public IReadOnlyList<int> ItemIds { get; init; } = [];
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
