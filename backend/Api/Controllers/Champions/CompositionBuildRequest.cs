namespace TrueMain.Controllers.Champions;

/// <summary>
/// Request body for <c>POST /champions/{id}/composition-build</c>. POST
/// because the input — up to nine champion/position slots across both teams —
/// is too rich for query parameters. Both slot lists are optional and may be
/// partial: the composition is a ranking signal, never a hard filter.
/// </summary>
public sealed record CompositionBuildRequest
{
    /// <summary>Position the player queues for. Required.</summary>
    public string? Position { get; init; }

    /// <summary>Optional patch filter (major.minor or full game version).</summary>
    public string? Patch { get; init; }

    /// <summary>Optional elo filter (band or <c>_PLUS</c> cumulative form).</summary>
    public string? EloBracket { get; init; }

    /// <summary>
    /// Known allied picks, excluding the player's own champion (a slot at the
    /// player's position is rejected — that slot is the route's champion).
    /// </summary>
    public IReadOnlyList<CompositionSlotInput> Allies { get; init; } = [];

    /// <summary>Known enemy picks.</summary>
    public IReadOnlyList<CompositionSlotInput> Enemies { get; init; } = [];
}

/// <summary>One known pick of the draft: a champion at a position.</summary>
public sealed record CompositionSlotInput
{
    public int ChampionId { get; init; }

    public string? Position { get; init; }
}
