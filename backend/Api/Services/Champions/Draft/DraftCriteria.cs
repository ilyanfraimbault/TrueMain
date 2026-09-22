namespace TrueMain.Services.Champions.Draft;

/// <summary>
/// One champion-select state, as the desktop app sees it.
/// </summary>
/// <remarks>
/// Every collection may be empty: picks arrive one at a time and the panel
/// re-solves on each one, so a criteria object with a single enemy champion is
/// the normal early-draft case and not a degraded input.
/// </remarks>
public sealed record DraftCriteria
{
    /// <summary>The player's own lane, normalised Riot team position.</summary>
    public required string Position { get; init; }

    /// <summary>Enemy champions picked or hovered so far, in any order.</summary>
    public IReadOnlyList<int> EnemyChampions { get; init; } = [];

    /// <summary>
    /// Enemy lanes the user corrected by hand, by champion id. Hard constraints
    /// on the guesser, which is what lets one correction fix several slots.
    /// </summary>
    public IReadOnlyDictionary<int, string> PinnedEnemyLanes { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    /// The enemy placement currently on screen, used only to break ties so the
    /// panel does not reshuffle between two picks.
    /// </summary>
    public IReadOnlyDictionary<int, string>? PreviousEnemyLanes { get; init; }

    /// <summary>Allies already locked, keyed by their lane. Our own slot is excluded.</summary>
    public IReadOnlyDictionary<string, int> Allies { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Champions banned this draft; never returned as candidates.</summary>
    public IReadOnlyList<int> Bans { get; init; } = [];

    /// <summary>
    /// The champions to rank — the player's own pool. Ranking every champion in
    /// the game would be noise: nobody first-times a champion because a number
    /// said so.
    /// </summary>
    public IReadOnlyList<int> Candidates { get; init; } = [];

    public string? Patch { get; init; }

    public string? EloBracket { get; init; }
}
