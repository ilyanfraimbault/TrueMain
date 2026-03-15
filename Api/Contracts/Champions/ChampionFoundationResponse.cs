namespace TrueMain.Contracts.Champions;

public sealed class ChampionFoundationResponse
{
    public ChampionSummaryResponse Summary { get; init; } = new();

    public ChampionHowToPlayFoundationResponse HowToPlay { get; init; } = new();
}

public sealed class ChampionSummaryResponse
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public double WinRate { get; init; }

    public int SpecialistCount { get; init; }

    public int OtpCount { get; init; }

    public string PrimaryPosition { get; init; } = string.Empty;

    public string LatestGameVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }
}

public sealed class ChampionHowToPlayFoundationResponse
{
    public int SampleSize { get; init; }

    public SummonerSpellOptionResponse? CoreSummonerSpells { get; init; }

    public SkillOrderOptionResponse? CoreSkillOrder { get; init; }

    public ItemSetOptionResponse? CoreItemSet { get; init; }

    public IReadOnlyList<SummonerSpellOptionResponse> SummonerSpellOptions { get; init; } = [];

    public IReadOnlyList<SkillOrderOptionResponse> SkillOrderOptions { get; init; } = [];

    public IReadOnlyList<ItemSetOptionResponse> ItemSetOptions { get; init; } = [];
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
