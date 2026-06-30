namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Top-level read model returned by <c>GET /champions/{id}</c>. Lists the
/// dominant <see cref="ChampionBuildReadModel"/> tabs for a champion at a
/// given patch + position, each tab keyed by (first completed item, primary
/// keystone).
/// </summary>
public sealed record ChampionResponse
{
    public int ChampionId { get; init; }

    public string Patch { get; init; } = string.Empty;

    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// Elo bracket this slice was computed for — one of
    /// <c>Core.Lol.Ranking.EloBracket</c> (<c>ALL</c> by default). The builds,
    /// skill orders and win rates below are scoped to this band.
    /// </summary>
    public string EloBracket { get; init; } = Core.Lol.Ranking.EloBracket.All;

    /// <summary>
    /// Games in the selected bracket as a fraction of all games on this
    /// champion at the resolved patch + position (across every bracket). Lets
    /// the page show how representative a high-bracket slice is. Always
    /// <c>1.0</c> for the <c>ALL</c> bracket.
    /// </summary>
    public double EloCoverage { get; init; } = 1d;

    /// <summary>
    /// False when <see cref="TotalGames"/> is below the minimum-sample floor
    /// for a trustworthy build (tiny high-bracket slices). The page still
    /// renders the data but flags it as low-confidence.
    /// </summary>
    public bool MinSampleMet { get; init; } = true;

    /// <summary>
    /// Total games across the scope, including patterns that are excluded
    /// from <see cref="Builds"/> (no first item, pick-rate below the floor,
    /// or beyond the top-N cap). Used as the denominator for
    /// <see cref="ChampionBuildReadModel.PickRate"/>.
    /// </summary>
    public int TotalGames { get; init; }

    /// <summary>
    /// Total wins across the same scope as <see cref="TotalGames"/>. Frontend
    /// derives the champion-wide win rate from <c>TotalWins / TotalGames</c>.
    /// </summary>
    public int TotalWins { get; init; }

    public IReadOnlyList<ChampionBuildReadModel> Builds { get; init; } = [];
}

/// <summary>
/// One tab on the champion page. Categorised by the combination of first
/// completed build item and primary keystone — both are required to be
/// non-zero. The tab body groups the slice's data into four sections:
/// <see cref="Core"/>, <see cref="Variations"/>, <see cref="BuildTree"/>,
/// and <see cref="RunePages"/>.
/// </summary>
public sealed record ChampionBuildReadModel
{
    public int FirstItemId { get; init; }

    public int PrimaryKeystoneId { get; init; }

    public int Games { get; init; }

    public double PickRate { get; init; }

    public double WinRate { get; init; }

    public BuildCoreReadModel Core { get; init; } = new();

    public BuildVariationsReadModel Variations { get; init; } = new();

    /// <summary>
    /// Item tree rooted at <see cref="FirstItemId"/>. The root itself is
    /// implicit (the tab already identifies it) — this is the list of its
    /// direct children, each recursively carrying its own children.
    /// </summary>
    public IReadOnlyList<BuildTreeNodeReadModel> BuildTree { get; init; } = [];

    /// <summary>
    /// Top rune pages within this slice. All entries share the tab's
    /// <see cref="PrimaryKeystoneId"/>; variations live on the perks,
    /// secondary tree, and stats.
    /// </summary>
    public IReadOnlyList<BuildRunePageReadModel> RunePages { get; init; } = [];
}

/// <summary>
/// Dominant single choice per dimension within a build slice — the "core"
/// view at the top of a tab. Each entry mirrors the equivalent top-1
/// alternative in <see cref="BuildVariationsReadModel"/>; the duplication is
/// deliberate so the frontend can render the prominent header and the full
/// list independently.
/// </summary>
public sealed record BuildCoreReadModel
{
    public BuildItemPathReadModel? ItemPath { get; init; }

    public BuildItemSetReadModel? Boots { get; init; }

    public BuildItemSetReadModel? StarterItems { get; init; }

    public BuildSummonerSpellsReadModel? SummonerSpells { get; init; }

    public BuildSkillOrderReadModel? SkillOrder { get; init; }

    public BuildRunePageReadModel? RunePage { get; init; }
}

/// <summary>
/// Top-N variations per dimension within the slice, including the dominant
/// (which also appears in <see cref="BuildCoreReadModel"/>). Rune pages are
/// exposed at the build level via <see cref="ChampionBuildReadModel.RunePages"/>,
/// not here.
/// </summary>
public sealed record BuildVariationsReadModel
{
    public IReadOnlyList<BuildItemSetReadModel> Boots { get; init; } = [];

    public IReadOnlyList<BuildItemSetReadModel> StarterItems { get; init; } = [];

    public IReadOnlyList<BuildSummonerSpellsReadModel> SummonerSpells { get; init; } = [];

    public IReadOnlyList<BuildSkillOrderReadModel> SkillOrder { get; init; } = [];
}

public sealed record BuildTreeNodeReadModel
{
    public int ItemId { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    /// <summary>
    /// Pick rate relative to the parent node — for a root child, this is
    /// <c>games / firstItem games</c>; for a deeper child, it is
    /// <c>games / parent.games</c>. Sums to ≤ 1 across siblings.
    /// </summary>
    public double PickRate { get; init; }

    public IReadOnlyList<BuildTreeNodeReadModel> Children { get; init; } = [];
}

public sealed record BuildItemPathReadModel
{
    public IReadOnlyList<int> ItemIds { get; init; } = [];

    public int Games { get; init; }

    public double PickRate { get; init; }

    public double WinRate { get; init; }
}

public sealed record BuildItemSetReadModel
{
    public IReadOnlyList<int> ItemIds { get; init; } = [];

    public int Games { get; init; }

    public double PickRate { get; init; }

    public double WinRate { get; init; }
}

public sealed record BuildSummonerSpellsReadModel
{
    public int Spell1Id { get; init; }

    public int Spell2Id { get; init; }

    public int Games { get; init; }

    public double PickRate { get; init; }

    public double WinRate { get; init; }
}

public sealed record BuildSkillOrderReadModel
{
    public IReadOnlyList<string> Sequence { get; init; } = [];

    public int Games { get; init; }

    public double PickRate { get; init; }

    public double WinRate { get; init; }
}

public sealed record BuildRunePageReadModel
{
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

    public double PickRate { get; init; }

    public double WinRate { get; init; }
}
