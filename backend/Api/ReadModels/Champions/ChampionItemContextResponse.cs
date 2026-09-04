namespace TrueMain.ReadModels.Champions;

/// <summary>
/// The situational build context of one champion slice (#1450, read surface #1451): for
/// each item the slice's builds actually reach, whether it is core, situational or a
/// preference, and — when situational — the draft situations that measurably move it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read straight from the verdicts.</b> Everything here was decided by the fold; this
/// response is a projection, with no statistics of its own. That is deliberate: the page
/// hovers an item and expects an answer, not a computation.
/// </para>
/// <para>
/// <b>Every rank together.</b> The verdicts carry no elo dimension — a situation is far
/// rarer than a champion, and splitting the games by rank starves the buckets the feature
/// rests on — so this response describes the whole population whatever rank the page
/// beside it is showing. <see cref="AllRanks"/> is that fact, carried explicitly so the
/// card can say it rather than letting a reader assume the filter applied.
/// </para>
/// </remarks>
public sealed record ChampionItemContextResponse
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>The patch the verdicts describe, resolved when the caller sent none.</summary>
    public string? Patch { get; init; }

    /// <summary>Always true today — see the remarks. Present so the client never has to assume it.</summary>
    public bool AllRanks { get; init; } = true;

    public IReadOnlyList<ChampionItemContextItemReadModel> Items { get; init; } = [];
}

/// <summary>One item's verdict.</summary>
public sealed record ChampionItemContextItemReadModel
{
    /// <summary><c>Build</c>, <c>Boots</c> or <c>Starter</c> — the decision this verdict is about.</summary>
    public string Slot { get; init; } = string.Empty;

    public int ItemId { get; init; }

    /// <summary><c>Core</c>, <c>Situational</c> or <c>Preference</c>.</summary>
    public string Class { get; init; } = string.Empty;

    /// <summary>Games of the slice that built this item.</summary>
    public int Games { get; init; }

    /// <summary>Games of the slot the pick rate is over.</summary>
    public int SlotGames { get; init; }

    public double PickRate { get; init; }

    /// <summary>Win rate of the games that built it. Null when the sample is empty.</summary>
    public double? WinRate { get; init; }

    /// <summary>
    /// Widest patch window behind the findings below: 1 when the served patch carried them
    /// on its own. The card prints it, because "over the last three patches" is a different
    /// claim from "this patch".
    /// </summary>
    public int PatchWindow { get; init; } = 1;

    /// <summary>Qualifying situations, strongest first. Empty for <c>Core</c> and <c>Preference</c>.</summary>
    public IReadOnlyList<ChampionItemContextAxisReadModel> Axes { get; init; } = [];
}

/// <summary>One situation that moves an item's pick rate.</summary>
public sealed record ChampionItemContextAxisReadModel
{
    /// <summary>The situation, e.g. <c>EnemyMagicDamage</c>. The client owns the wording.</summary>
    public string Axis { get; init; } = string.Empty;

    /// <summary><c>High</c> or <c>Low</c> — the end of the axis where the item is picked more.</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>
    /// Whether a draft alone determines this situation. False for the gold-lead axis, which
    /// a reader can only act on once the game is under way — the card has to say which kind
    /// of advice it is giving.
    /// </summary>
    public bool DraftTime { get; init; }

    public int GamesIn { get; init; }

    public int TotalIn { get; init; }

    public int GamesOut { get; init; }

    public int TotalOut { get; init; }

    /// <summary>Pick rate inside the bucket.</summary>
    public double RateIn { get; init; }

    /// <summary>Pick rate at the opposite end.</summary>
    public double RateOut { get; init; }

    /// <summary><see cref="RateIn"/> minus <see cref="RateOut"/>, always positive.</summary>
    public double Lift { get; init; }

    /// <summary>Patches folded into this finding — 1 when the served patch was deep enough alone.</summary>
    public int PatchWindow { get; init; } = 1;
}
