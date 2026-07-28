namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// "How well does this player actually play this champion" — the aggregate of
/// TrueMain's per-match performance score over the player's recent games on it,
/// backing <c>GET /truemains/{nameTag}/champions/{championId}/performance</c>.
///
/// <para>Always a 200 for a known account, even with no usable sample: the
/// counts are the payload, and a thin sample is reported honestly through
/// <see cref="Games"/> versus <see cref="MinGames"/> rather than hidden behind a
/// confident-looking average. Below the floor every score field is
/// <c>null</c>.</para>
/// </summary>
public sealed record PlayerChampionPerformanceResponse
{
    public int ChampionId { get; init; }

    /// <summary>The lane the sample was scoped to, or null when every lane was counted.</summary>
    public string? Position { get; init; }

    /// <summary>The major.minor patch the sample was scoped to, or null for every patch.</summary>
    public string? Patch { get; init; }

    /// <summary>Ranked solo/duo games on the champion that were actually graded.</summary>
    public int Games { get; init; }

    /// <summary>
    /// Sample floor: below this many games the averages are suppressed. Exposed
    /// so the frontend can word the empty state with the real number instead of
    /// hardcoding one that drifts.
    /// </summary>
    public int MinGames { get; init; }

    /// <summary>
    /// Most recent games the panel ever looks at. The score is a form metric,
    /// not a career one — and the window bounds the read's cost.
    /// </summary>
    public int Window { get; init; }

    /// <summary>Mean 0–100 score over <see cref="Games"/>. Null below the floor.</summary>
    public double? AverageScore { get; init; }

    /// <summary>Best single game in the window. Null below the floor.</summary>
    public int? BestScore { get; init; }

    /// <summary>Worst single game in the window. Null below the floor.</summary>
    public int? WorstScore { get; init; }

    /// <summary>
    /// Share of the graded games the player topped their own side in (MVP on a
    /// win, ACE on a loss), 0..1. Null below the floor.
    /// </summary>
    public double? TopOfTeamRate { get; init; }

    /// <summary>
    /// Per-component averages, one entry per component of the model, in the
    /// model's own order. Empty below the floor.
    /// </summary>
    public IReadOnlyList<PlayerChampionPerformanceComponent> Components { get; init; }
        = Array.Empty<PlayerChampionPerformanceComponent>();
}

/// <summary>
/// One component of the score averaged over the sample. A component is only
/// averaged over the games it was actually available in — a game with no
/// timeline coverage is excluded from the laning average rather than counted as
/// a zero — so <see cref="Games"/> can be lower than the response's total and is
/// reported alongside the value.
/// </summary>
public sealed record PlayerChampionPerformanceComponent
{
    /// <summary>
    /// The component's name, matching <c>Core.Lol.Performance.PerformanceComponentKind</c>
    /// (<c>Combat</c>, <c>KillParticipation</c>, <c>DamageShare</c>, <c>GoldShare</c>,
    /// <c>Farming</c>, <c>Vision</c>, <c>Laning</c>, <c>MidGame</c>, <c>Roam</c>).
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// The role profile's nominal weight for this component on the 0..100 scale,
    /// averaged over the sample (a player who switched lanes mid-sample lands
    /// between the two profiles). Zero means the role does not grade it at all.
    /// </summary>
    public double Weight { get; init; }

    /// <summary>Mean 0..1 grade over the games where the component was available. Null when it never was.</summary>
    public double? Value { get; init; }

    /// <summary>Games the component was available in — the denominator of <see cref="Value"/>.</summary>
    public int Games { get; init; }
}
