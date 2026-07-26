namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// TrueMain's dedication score for a player's signature champion, plus the raw
/// inputs and the normalised components that produced it. The whole breakdown
/// ships so the UI can explain the number in a tooltip instead of presenting a
/// bare figure — the formula lives in <see cref="Core.Truemains.DedicationScore"/>
/// and is documented in <c>docs/dedication-score.md</c>.
/// </summary>
public sealed record DedicationReadModel
{
    /// <summary>Final score, 0..100 (one decimal). Higher = more devoted to <see cref="ChampionId"/>.</summary>
    public double Score { get; init; }

    /// <summary>The champion the score is about: the player's most-played main (their signature champion).</summary>
    public int ChampionId { get; init; }

    /// <summary>Share component (0..1): the player's play rate on the champion, rescaled from the main-analysis play-rate floor.</summary>
    public double Commitment { get; init; }

    /// <summary>Time-span component (0..1): distinct tracked patches played on the champion, saturating at the span target.</summary>
    public double Span { get; init; }

    /// <summary>Sample-size component (0..1): career games on the champion on a log curve, saturating at the volume target.</summary>
    public double Volume { get; init; }

    /// <summary>Recency component (0..1): exponential decay on days since the last tracked game on the champion.</summary>
    public double Recency { get; init; }

    /// <summary>Raw share of the player's recent ranked games spent on the champion (0..1), straight from main analysis.</summary>
    public double PlayRate { get; init; }

    /// <summary>Raw tracked ranked games on the champion, summed across every aggregated patch.</summary>
    public int CareerGames { get; init; }

    /// <summary>Raw count of distinct patches TrueMain has seen the player play the champion on.</summary>
    public int PatchSpan { get; init; }

    /// <summary>
    /// Whole days since the player's last tracked game on the champion. Null
    /// when no aggregated game exists yet (a freshly discovered account), in
    /// which case <see cref="Recency"/> is 0.
    /// </summary>
    public int? DaysSinceLastGame { get; init; }
}
