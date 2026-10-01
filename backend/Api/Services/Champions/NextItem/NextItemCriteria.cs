namespace TrueMain.Services.Champions.NextItem;

/// <summary>One other participant of the game: a champion, and its lane when known.</summary>
public sealed record NextItemParticipant(int ChampionId, string? Position);

/// <summary>
/// One live game as the desktop app sees it when an item changes hands (#1749).
/// </summary>
public sealed record NextItemCriteria
{
    /// <summary>The player's own lane, normalised Riot team position.</summary>
    public required string Position { get; init; }

    public string? Patch { get; init; }

    /// <summary>
    /// The items the player holds or has completed, in the order they were completed when
    /// the client knows it. Components, consumables and anything the model does not know
    /// are ignored; the order only matters among completed items.
    /// </summary>
    public IReadOnlyList<int> Items { get; init; } = [];

    /// <summary>The four teammates; a missing lane falls back to the champion's best-covered profile.</summary>
    public IReadOnlyList<NextItemParticipant> Allies { get; init; } = [];

    /// <summary>The five enemies; a missing lane is placed by the draft's lane solver around the given ones.</summary>
    public IReadOnlyList<NextItemParticipant> Enemies { get; init; } = [];

    /// <summary>Our gold lead over the lane opponent at 15 minutes, once the game is past it.</summary>
    public double? GoldLeadAt15 { get; init; }
}
