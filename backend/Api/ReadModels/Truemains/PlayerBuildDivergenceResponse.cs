namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Canonical <see cref="BuildDivergenceReadModel.Dimension"/> values. The
/// frontend switches its copy and its icon rendering on these, so they are part
/// of the API contract — rename one and the "you vs mains" card stops
/// recognising the row.
/// </summary>
public static class BuildDivergenceDimensions
{
    /// <summary>Starter-item set bought in the opening window.</summary>
    public const string StarterItems = "starterItems";

    /// <summary>The single pair of boots completed in the game.</summary>
    public const string Boots = "boots";

    /// <summary>
    /// The first completed build items, in completion order (boots excluded —
    /// they live in their own dimension).
    /// </summary>
    public const string ItemPath = "itemPath";

    /// <summary>Max order of the three basic spells, e.g. <c>Q-E-W</c>.</summary>
    public const string SkillOrder = "skillOrder";
}

/// <summary>
/// "You vs mains" read model returned by
/// <c>GET /truemains/{nameTag}/champions/{championId}/divergence</c>: how one
/// player's habits on a champion compare to what the champion's mains actually
/// do at the same patch and position.
///
/// Pure overlay on the existing aggregates — the player side reads that
/// player's <c>champion_aggregate_*</c> slice, the mains side reads the same
/// slice across every <em>other</em> account. No new table, no live match scan.
/// </summary>
public sealed record PlayerBuildDivergenceResponse
{
    public int ChampionId { get; init; }

    /// <summary>
    /// Patch both sides were computed for. Resolved from the player's slice
    /// (the most recent patch where they actually have games), then pinned on
    /// the mains side so the two are always comparable.
    /// </summary>
    public string Patch { get; init; } = string.Empty;

    /// <summary>
    /// Position both sides were computed for — the player's dominant lane on
    /// the champion unless the caller pinned one.
    /// </summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>Games the player has in the resolved slice.</summary>
    public int PlayerGames { get; init; }

    /// <summary>
    /// Games the reference pool has in the same slice. The player's own games
    /// are excluded, so "x% of mains" never partly means "x% of you".
    /// </summary>
    public int MainsGames { get; init; }

    /// <summary>
    /// Distinct accounts behind <see cref="MainsGames"/>. Lets the card say how
    /// many mains the comparison is drawn from instead of implying a crowd that
    /// might be three people.
    /// </summary>
    public int MainsPlayers { get; init; }

    /// <summary>
    /// Games the player needs in the slice before the comparison is drawn.
    /// Echoed so the empty state can show the real bar rather than hardcoding it.
    /// </summary>
    public int MinPlayerGames { get; init; }

    /// <summary>Games the reference pool needs before it is worth comparing to.</summary>
    public int MinMainsGames { get; init; }

    /// <summary>
    /// <see cref="PlayerGames"/> cleared <see cref="MinPlayerGames"/>. False
    /// means the honest empty state: we know the player plays the champion, we
    /// just refuse to coach them off three games.
    /// </summary>
    public bool MinSampleMet { get; init; }

    /// <summary>
    /// <see cref="MainsGames"/> cleared <see cref="MinMainsGames"/>. False on a
    /// niche champion / lane where there is no meaningful "what mains do" to
    /// compare against.
    /// </summary>
    public bool ReferenceSampleMet { get; init; }

    /// <summary>
    /// One row per compared dimension, or empty when either sample floor is
    /// missed. Ordered most-actionable first: diverging rows before matching
    /// ones, then by how strongly the mains agree on their own choice (a
    /// dimension the mains are split on is weaker advice than one they are
    /// unanimous about).
    /// </summary>
    public IReadOnlyList<BuildDivergenceReadModel> Dimensions { get; init; } = [];
}

/// <summary>
/// One compared dimension: the player's dominant choice, the mains' dominant
/// choice, and how common the player's choice is among the mains.
/// </summary>
public sealed record BuildDivergenceReadModel
{
    /// <summary>One of the <see cref="BuildDivergenceDimensions"/> constants.</summary>
    public string Dimension { get; init; } = string.Empty;

    /// <summary>
    /// The two dominant choices differ. Matching rows are still returned — a
    /// card that only ever lists mistakes reads as an indictment rather than a
    /// comparison.
    /// </summary>
    public bool Diverges { get; init; }

    /// <summary>The player's most-frequent choice in the slice.</summary>
    public BuildChoiceReadModel Player { get; init; } = new();

    /// <summary>The mains' most-frequent choice in the same slice.</summary>
    public BuildChoiceReadModel Mains { get; init; } = new();

    /// <summary>
    /// Mains' games that made the <em>player's</em> choice. The other half of
    /// the sentence: not just "62% of mains go Y", but "and only 4% go X like
    /// you". Equals <see cref="Mains"/>'s counts when the row does not diverge.
    /// </summary>
    public int MainsGamesOnPlayerChoice { get; init; }

    /// <summary>
    /// <see cref="MainsGamesOnPlayerChoice"/> over the mains' total games in
    /// the slice.
    /// </summary>
    public double MainsRateOnPlayerChoice { get; init; }

    /// <summary>
    /// Win rate the mains post on the player's choice, or <see langword="null"/>
    /// when no mains game made it. Keeps the card from implying a choice is bad
    /// when it is merely rare.
    /// </summary>
    public double? MainsWinRateOnPlayerChoice { get; init; }
}

/// <summary>
/// A single dominant choice inside one pool (the player, or the mains).
/// <see cref="ItemIds"/> carries the choice for the item dimensions and
/// <see cref="Skills"/> for <see cref="BuildDivergenceDimensions.SkillOrder"/> —
/// exactly one of the two is ever populated.
/// </summary>
public sealed record BuildChoiceReadModel
{
    /// <summary>
    /// Starter set, the single boots id, or the completed item path in order.
    /// Empty for the skill-order dimension.
    /// </summary>
    public IReadOnlyList<int> ItemIds { get; init; } = [];

    /// <summary>
    /// Max order of the basic spells (<c>["Q", "E", "W"]</c>), first maxed
    /// first. Empty for the item dimensions.
    /// </summary>
    public IReadOnlyList<string> Skills { get; init; } = [];

    /// <summary>Games in the pool that made this choice.</summary>
    public int Games { get; init; }

    /// <summary>
    /// <see cref="Games"/> over the pool's total games in the slice — how
    /// committed the pool is to this choice.
    /// </summary>
    public double PickRate { get; init; }

    /// <summary>Win rate over <see cref="Games"/>.</summary>
    public double WinRate { get; init; }
}
