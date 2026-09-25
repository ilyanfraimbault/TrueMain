namespace Core.Truemains;

/// <summary>
/// Raw, storage-shaped inputs of the truemain score, all measured for one
/// (player, champion) pair — the player's signature champion.
/// </summary>
/// <param name="PlayRate">
/// Share of the player's recent ranked games spent on the champion (0..1), as
/// stored by main analysis on <c>main_champion_stats.PlayRate</c>.
/// </param>
/// <param name="MasteryPoints">
/// Riot champion-mastery points on the champion — the player's lifetime
/// investment in it, across every queue and independent of how long TrueMain
/// has tracked the account. Null when the mastery has not been read yet.
/// </param>
/// <param name="MasteryRank">
/// 1-based rank of the champion in the player's mastery list, ordered by
/// points (1 = the champion they have played most, ever). Null when the
/// mastery has not been read yet or has no entry for the champion.
/// </param>
public readonly record struct DedicationInputs(
    double PlayRate,
    long? MasteryPoints,
    int? MasteryRank);

/// <summary>
/// The truemain score plus every component that produced it, so the surface
/// rendering it can explain the number instead of asserting it.
/// </summary>
/// <param name="Score">Final score, 0..100, rounded to one decimal.</param>
/// <param name="Commitment">Play-rate component, 0..1.</param>
/// <param name="Mastery">Mastery-points component, 0..1.</param>
/// <param name="MasteryRank">Mastery-rank component, 0..1.</param>
public readonly record struct DedicationBreakdown(
    double Score,
    double Commitment,
    double Mastery,
    double MasteryRank);

/// <summary>
/// TrueMain's signature metric: how much of a truemain a player is on a single
/// champion, on a 0..100 scale. Shown as the <em>Truemain score</em>; the code
/// keeps its original <c>dedication</c> name (#1701).
/// </summary>
/// <remarks>
/// <para>
/// The score is a weighted arithmetic mean of three components, each normalised
/// to 0..1 and each monotone in its input:
/// </para>
/// <code>
/// score = 100 x ( 0.55 * commitment
///               + 0.30 * mastery
///               + 0.15 * masteryRank )
///
/// commitment  = clamp01( (playRate - 0.12) / (1 - 0.12) )
/// mastery     = clamp01( ln(points / 50 000) / ln(3 000 000 / 50 000) )
/// masteryRank = 1 / rank
/// </code>
/// <para>
/// Two questions, nothing else: does the player give this champion their games
/// <em>now</em> (commitment), and has it been theirs for a long time (mastery
/// points and rank). Nothing here depends on how long TrueMain has tracked the
/// account, and activity is not a component — inactive mains are excluded by
/// <c>IsActive</c> upstream rather than decayed here.
/// </para>
/// <para>
/// The constants are the calibration surface — see <c>docs/dedication-score.md</c>
/// for the reasoning behind each one.
/// </para>
/// </remarks>
public static class DedicationScore
{
    /// <summary>Weight of the play-rate component. The dominant signal: a truemain is first of all someone who gives the champion their games.</summary>
    public const double CommitmentWeight = 0.55;

    /// <summary>Weight of the mastery-points component: how long the champion has been theirs.</summary>
    public const double MasteryWeight = 0.30;

    /// <summary>Weight of the mastery-rank component: whether it is the champion they have played most, ever.</summary>
    public const double MasteryRankWeight = 0.15;

    /// <summary>
    /// Default play rate at which commitment reads 0, and the value
    /// <c>MainAnalysis:PlayRateFloor</c> itself defaults to: below it a champion
    /// is not classified as a main at all, so no truemain can sit under it and
    /// the range under the floor is dead scale.
    /// </summary>
    /// <remarks>
    /// This is only the default. Callers that can see configuration pass the
    /// live <c>MainAnalysis:PlayRateFloor</c> into <see cref="Compute"/>, so
    /// retuning the classification floor moves the score with it (#869).
    /// </remarks>
    public const double CommitmentFloor = 0.12;

    /// <summary>Mastery points at which the mastery component reads 0 — roughly the 5th percentile of tracked mains.</summary>
    public const long MasteryFloorPoints = 50_000;

    /// <summary>Mastery points at which the mastery component saturates — past the 90th percentile of tracked mains.</summary>
    public const long MasteryTargetPoints = 3_000_000;

    /// <summary>
    /// Scores one (player, champion) pair. Pure: same inputs, same output, no
    /// clock and no I/O.
    /// </summary>
    /// <param name="inputs">The player's measured history on the champion.</param>
    /// <param name="commitmentFloor">
    /// Play rate at which commitment reads 0 — the live
    /// <c>MainAnalysis:PlayRateFloor</c>. Defaults to <see cref="CommitmentFloor"/>,
    /// which is that option's own default.
    /// </param>
    public static DedicationBreakdown Compute(DedicationInputs inputs, double commitmentFloor = CommitmentFloor)
    {
        var commitment = Commitment(inputs.PlayRate, commitmentFloor);
        var mastery = Mastery(inputs.MasteryPoints);
        var masteryRank = MasteryRank(inputs.MasteryRank);

        var weighted = (CommitmentWeight * commitment)
                       + (MasteryWeight * mastery)
                       + (MasteryRankWeight * masteryRank);

        // The weights sum to 1, so `weighted` is already 0..1; the clamp is a
        // guard against a future re-weighting drifting off 1, not a live case.
        // Rounded to one decimal so the number the leaderboard sorts on is the
        // number it prints.
        return new DedicationBreakdown(
            Score: Math.Round(100d * Clamp01(weighted), 1, MidpointRounding.AwayFromZero),
            Commitment: commitment,
            Mastery: mastery,
            MasteryRank: masteryRank);
    }

    /// <summary>Share of the player's games on the champion, rescaled so <paramref name="commitmentFloor"/> reads 0 and a pure one-trick reads 1.</summary>
    public static double Commitment(double playRate, double commitmentFloor = CommitmentFloor)
    {
        if (double.IsNaN(playRate))
        {
            return 0d;
        }

        // A floor outside [0, 1) would invert the rescale or divide by zero.
        // Both hosts range-check MainAnalysis:PlayRateFloor at startup, so this
        // is defence in depth for a caller passing a raw value rather than the
        // configured one.
        if (double.IsNaN(commitmentFloor) || commitmentFloor < 0d || commitmentFloor >= 1d)
        {
            commitmentFloor = CommitmentFloor;
        }

        return Clamp01((playRate - commitmentFloor) / (1d - commitmentFloor));
    }

    /// <summary>
    /// Mastery points on a log scale between <see cref="MasteryFloorPoints"/> (0)
    /// and <see cref="MasteryTargetPoints"/> (1). Unread mastery scores 0 — the
    /// surface says it is pending rather than inventing a value.
    /// </summary>
    public static double Mastery(long? masteryPoints)
    {
        if (masteryPoints is not { } points || points <= MasteryFloorPoints)
        {
            return 0d;
        }

        return Clamp01(Math.Log((double)points / MasteryFloorPoints)
                       / Math.Log((double)MasteryTargetPoints / MasteryFloorPoints));
    }

    /// <summary>1 for the player's most-mastered champion, ½ for the second, ⅓ for the third…; 0 when unknown.</summary>
    public static double MasteryRank(int? masteryRank)
        => masteryRank is { } rank && rank >= 1 ? 1d / rank : 0d;

    private static double Clamp01(double value)
        => double.IsNaN(value) ? 0d : Math.Clamp(value, 0d, 1d);
}
