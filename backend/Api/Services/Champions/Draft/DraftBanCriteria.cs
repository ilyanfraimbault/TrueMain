namespace TrueMain.Services.Champions.Draft;

/// <summary>A champion of the player's pool and how much it weighs (mastery points).</summary>
public sealed record DraftPoolEntry(int ChampionId, double Weight);

/// <summary>
/// One champion-select state, as the ban suggestions need it (#1906).
/// </summary>
public sealed record DraftBanCriteria
{
    /// <summary>The player's own lane, normalised Riot team position.</summary>
    public required string Position { get; init; }

    /// <summary>
    /// The pick the player declared — their own hover during the planning phase —
    /// which the bans protect when present.
    /// </summary>
    public int? PlannedPick { get; init; }

    /// <summary>The player's pool, protected (mastery-weighted) when no pick is declared.</summary>
    public IReadOnlyList<DraftPoolEntry> Pool { get; init; } = [];

    /// <summary>Allies' champions, locked or hovered: never suggested as a ban.</summary>
    public IReadOnlyList<int> AllyChampions { get; init; } = [];

    /// <summary>Enemy champions already picked: no longer bannable.</summary>
    public IReadOnlyList<int> EnemyChampions { get; init; } = [];

    /// <summary>Champions already banned.</summary>
    public IReadOnlyList<int> Bans { get; init; } = [];

    public string? Patch { get; init; }

    public string? EloBracket { get; init; }
}
