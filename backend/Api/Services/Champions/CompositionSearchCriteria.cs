namespace TrueMain.Services.Champions;

/// <summary>
/// Input of the composition match search: the champion the player is on, their
/// position, and the (possibly partial) draft around them. Slots are keyed by
/// Riot team position (<c>TOP</c>, <c>JUNGLE</c>, <c>MIDDLE</c>, <c>BOTTOM</c>,
/// <c>UTILITY</c>) — the caller normalises raw input before building this.
/// The composition is a ranking signal, never a hard filter: any subset of
/// slots (including none) is valid and only narrows the ordering.
/// </summary>
public sealed record CompositionSearchCriteria
{
    public required int ChampionId { get; init; }

    /// <summary>Normalised Riot team position of the player.</summary>
    public required string Position { get; init; }

    /// <summary>
    /// Ally champions by position, excluding the player's own slot (an entry at
    /// the player's position is ignored — that slot is the hard-filtered
    /// champion itself).
    /// </summary>
    public IReadOnlyDictionary<string, int> Allies { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Enemy champions by position.</summary>
    public IReadOnlyDictionary<string, int> Enemies { get; init; } =
        new Dictionary<string, int>();

    /// <summary>
    /// Optional patch narrowing; accepts either <c>major.minor</c> or a full
    /// Riot game version (normalised by the service).
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// Optional elo filter (band or <c>_PLUS</c> cumulative form), resolved to
    /// its bands by the service; null means every bracket.
    /// </summary>
    public string? EloBracket { get; init; }
}
