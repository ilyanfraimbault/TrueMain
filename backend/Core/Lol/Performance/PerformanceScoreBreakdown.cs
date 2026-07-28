namespace Core.Lol.Performance;

/// <summary>The nine graded axes of <see cref="PerformanceScore"/>.</summary>
public enum PerformanceComponentKind
{
    /// <summary>KDA, capped so one blowout line cannot swamp the rest.</summary>
    Combat,

    /// <summary>Share of the team's kills the player took part in.</summary>
    KillParticipation,

    /// <summary>Share of the team's damage to champions.</summary>
    DamageShare,

    /// <summary>Share of the team's earned gold.</summary>
    GoldShare,

    /// <summary>CS per minute against a role reference.</summary>
    Farming,

    /// <summary>Vision score per minute against a role reference.</summary>
    Vision,

    /// <summary>Leads over the lane opponent at the laning-phase marks (≤ 15 min).</summary>
    Laning,

    /// <summary>Leads over the lane opponent at the post-laning marks (&gt; 15 min).</summary>
    MidGame,

    /// <summary>Early kill participations made outside the player's own lane.</summary>
    Roam,
}

/// <summary>
/// One graded axis of a <see cref="PerformanceScoreBreakdown"/>: what the role
/// profile weights it at, what the player scored on it, and how much of the
/// published score it ended up carrying once the dropped components' weight was
/// redistributed.
/// </summary>
public sealed record PerformanceComponentScore
{
    public PerformanceComponentKind Kind { get; init; }

    /// <summary>
    /// The role profile's nominal weight for this component, on the 0..100 scale
    /// the profiles are written in. Constant for a given position — it does not
    /// change with the player's stat line.
    /// </summary>
    public double Weight { get; init; }

    /// <summary>
    /// The 0..1 grade, or <c>null</c> when the input this component needs is
    /// missing and the component was therefore dropped rather than scored 0.
    /// </summary>
    public double? Value { get; init; }

    /// <summary>
    /// Fraction of the published score this component actually carried, 0..1.
    /// Zero for a dropped component; the non-zero entries sum to 1. This is
    /// <see cref="Weight"/> renormalised over the components that survived, so
    /// <c>sum(EffectiveWeight × Value) × 100</c> reproduces the score.
    /// </summary>
    public double EffectiveWeight { get; init; }
}

/// <summary>
/// The published score together with the per-component detail behind it — the
/// explainable form of <see cref="PerformanceScore.Compute"/>, used by the
/// surfaces that show <em>why</em> a game graded the way it did rather than just
/// the number.
/// </summary>
public sealed record PerformanceScoreBreakdown
{
    /// <summary>The 0–100 score. Identical to what <see cref="PerformanceScore.Compute"/> returns.</summary>
    public int Score { get; init; }

    /// <summary>
    /// One entry per <see cref="PerformanceComponentKind"/>, always all nine and
    /// always in enum order, so a caller can index them positionally. Dropped
    /// components are present with a <c>null</c> value.
    /// </summary>
    public IReadOnlyList<PerformanceComponentScore> Components { get; init; }
        = Array.Empty<PerformanceComponentScore>();
}
