namespace TrueMain.ReadModels.Champions;

/// <summary>
/// What to complete next in one live game (#1749): for each open slot, the items the
/// champion's mains continue into from where this game stands, ranked by the share the
/// model predicts for a game in this one's situation.
/// </summary>
/// <remarks>
/// The ranking is a <b>prediction of the mains' choice</b>, not a win-rate argmax: an item's
/// win rate is mostly a statement about the game state it gets bought in. The games and wins
/// are carried as measured facts beside it.
/// </remarks>
public sealed record ChampionNextItemResponse
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>The patch the model was served for, resolved when the caller sent none; null when the champion has no model at this position.</summary>
    public string? Patch { get; init; }

    /// <summary>The next legendary item, or null when the build has no open branch left.</summary>
    public NextItemSlotReadModel? Build { get; init; }

    /// <summary>The boots, or null when the game already holds a pair the mains settle on.</summary>
    public NextItemSlotReadModel? Boots { get; init; }

    /// <summary>Where the game sits on every situation the model could evaluate, axis name to bucket.</summary>
    public IReadOnlyDictionary<string, string> Situation { get; init; } = new Dictionary<string, string>();

    /// <summary>The enemy lanes the situation was computed against, the caller's own first, then the solver's.</summary>
    public IReadOnlyList<NextItemEnemyLaneReadModel> EnemyLanes { get; init; } = [];
}

/// <summary>One decision: the branch the game is on and its ranked candidates.</summary>
public sealed record NextItemSlotReadModel
{
    /// <summary>The item the decision follows, 0 at the root of the build.</summary>
    public int ParentItemId { get; init; }

    /// <summary>
    /// False when the build left the mains' tree — it completed an item the mains never take
    /// at that step — and the branch was re-anchored on its latest item that is one.
    /// </summary>
    public bool OnTree { get; init; } = true;

    /// <summary>Games of the branch behind the base shares.</summary>
    public int BranchGames { get; init; }

    /// <summary>Patches behind the base shares, the served one included.</summary>
    public int PatchWindow { get; init; } = 1;

    public IReadOnlyList<NextItemCandidateReadModel> Candidates { get; init; } = [];
}

/// <summary>One item the game could complete next.</summary>
public sealed record NextItemCandidateReadModel
{
    public int ItemId { get; init; }

    /// <summary>Predicted share of the branch for a game in this situation, over the candidates left.</summary>
    public double Share { get; init; }

    /// <summary>Share of the branch whatever the game — what the build tree on the champion page shows.</summary>
    public double BaseShare { get; init; }

    /// <summary>Games of the branch that took it.</summary>
    public int Games { get; init; }

    /// <summary>Of which won — a measured fact, never the ranking key.</summary>
    public int Wins { get; init; }

    /// <summary>The situations that moved this item most, largest shift first, at most three.</summary>
    public IReadOnlyList<NextItemReasonReadModel> Reasons { get; init; } = [];
}

/// <summary>One situation that moved an item: <c>EnemyMagicDamage</c> / <c>High</c>, and by how much.</summary>
public sealed record NextItemReasonReadModel
{
    /// <summary>The axis, named as the item-context read names it, so the site's wording applies.</summary>
    public string Axis { get; init; } = string.Empty;

    /// <summary><c>Low</c> or <c>High</c> — the middle of an axis moves a share but has no wording, so it is never a reason.</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>Factor applied to the item's share by this situation (1.4 = forty percent more often).</summary>
    public double Factor { get; init; }
}

/// <summary>An enemy and the lane the situation placed it on.</summary>
public sealed record NextItemEnemyLaneReadModel
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>True when the caller sent the lane, false when the lane solver guessed it.</summary>
    public bool Given { get; init; }
}
