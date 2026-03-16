namespace TrueMain.ReadModels.Champions;

public sealed class ChampionFoundationReadModel
{
    public ChampionSummaryReadModel Summary { get; init; } = new();

    public ChampionHowToPlayFoundationReadModel HowToPlay { get; init; } = new();
}

public sealed class ChampionSummaryReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public double WinRate { get; init; }

    public int SpecialistCount { get; init; }

    public int OtpCount { get; init; }

    public string PrimaryPosition { get; init; } = string.Empty;

    public string LatestPatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }
}

public sealed class ChampionHowToPlayFoundationReadModel
{
    public int SampleSize { get; init; }

    public SummonerSpellOptionReadModel? CoreSummonerSpells { get; init; }

    public SkillOrderOptionReadModel? CoreSkillOrder { get; init; }

    public ItemSetOptionReadModel? CoreItemSet { get; init; }

    public IReadOnlyList<SummonerSpellOptionReadModel> SummonerSpellOptions { get; init; } = [];

    public IReadOnlyList<SkillOrderOptionReadModel> SkillOrderOptions { get; init; } = [];

    public IReadOnlyList<ItemSetOptionReadModel> ItemSetOptions { get; init; } = [];
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
