namespace Core.Truemains;

/// <summary>
/// Raw, storage-shaped inputs of the dedication score, all measured for one
/// (player, champion) pair — the player's signature champion.
/// </summary>
/// <param name="PlayRate">
/// Share of the player's recent ranked games spent on the champion (0..1), as
/// stored by main analysis on <c>main_champion_stats.PlayRate</c>.
/// </param>
/// <param name="CareerGames">
/// Total tracked ranked games on the champion, summed across every frozen
/// <c>champion_aggregate_scopes</c> slice. Negative values are treated as 0.
/// </param>
/// <param name="PatchSpan">
/// Number of distinct game patches we have seen the player play the champion
/// on (<c>COUNT(DISTINCT GameVersion)</c> over the same scopes).
/// </param>
/// <param name="DaysSinceLastGame">
/// Days between the champion's most recent tracked game and "now". Negative
/// values (clock skew, a game timestamped in the future) are treated as 0.
/// </param>
public readonly record struct DedicationInputs(
    double PlayRate,
    int CareerGames,
    int PatchSpan,
    double DaysSinceLastGame);

/// <summary>
/// The dedication score plus every component that produced it, so the surface
/// rendering it can explain the number instead of asserting it.
/// </summary>
/// <param name="Score">Final score, 0..100, rounded to one decimal.</param>
/// <param name="Commitment">Share component, 0..1.</param>
/// <param name="Span">Time-span component, 0..1.</param>
/// <param name="Volume">Sample-size component, 0..1.</param>
/// <param name="Recency">Recency component, 0..1.</param>
public readonly record struct DedicationBreakdown(
    double Score,
    double Commitment,
    double Span,
    double Volume,
    double Recency);

/// <summary>
/// TrueMain's signature metric: how devoted a player is to a single champion,
/// on a 0..100 scale.
/// </summary>
/// <remarks>
/// <para>
/// The score is a weighted arithmetic mean of four independent components, each
/// normalised to 0..1 and each monotone in its input:
/// </para>
/// <code>
/// score = 100 x ( 0.45 * commitment
///               + 0.20 * span
///               + 0.20 * volume
///               + 0.15 * recency )
///
/// commitment = clamp01( (playRate - 0.12) / (1 - 0.12) )
/// span       = clamp01( patchSpan / 6 )
/// volume     = clamp01( ln(1 + careerGames) / ln(1 + 200) )
/// recency    = clamp01( 0.5 ^ (daysSinceLastGame / 21) )
/// </code>
/// <para>
/// Why a weighted mean rather than a product: no single missing signal should
/// zero a player out. A genuine one-trick whose aggregates have not been built
/// yet (volume/span still 0) keeps the commitment points they earned, and a
/// long-tracked veteran who took a break keeps their span and volume while the
/// recency term decays. That also keeps the metric robust to an ingestion
/// stall, which would otherwise decay every player's recency at once and
/// scramble the whole ranking.
/// </para>
/// <para>
/// Why these shapes:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>commitment</b> is rescaled from <see cref="CommitmentFloor"/> instead of
/// from 0 because every classified main already clears roughly that play rate
/// (main analysis relaxes its threshold down to <c>MainAnalysis:PlayRateFloor</c>,
/// 0.12). Rescaling spends the whole 0..1 range on the interval that actually
/// occurs instead of leaving the bottom eighth of the scale unreachable.
/// </item>
/// <item>
/// <b>span</b> counts distinct patches, not calendar days: a player who has
/// stuck with the champion across six patches has survived six rounds of
/// balance changes, which is the honest measure of "still their champion". It
/// can only count patches TrueMain has tracked, so a freshly discovered account
/// legitimately starts near 0 and climbs as we observe it.
/// </item>
/// <item>
/// <b>volume</b> is logarithmic: the difference between 10 and 60 games says far
/// more about devotion than the difference between 400 and 450, and a log curve
/// keeps a high-volume outlier from flattening everyone else.
/// </item>
/// <item>
/// <b>recency</b> is an exponential half-life rather than a cliff, so a player
/// slides down the board gradually instead of dropping off it the day after an
/// arbitrary cutoff.
/// </item>
/// </list>
/// <para>
/// The constants are the calibration surface — see <c>docs/dedication-score.md</c>
/// for the reasoning behind each one. Changing a weight changes every score, so
/// they live here as the single source of truth rather than being restated at
/// each call site.
/// </para>
/// </remarks>
public static class DedicationScore
{
    /// <summary>Weight of the "share of games" component. The dominant signal: dedication is first of all what fraction of your games you give the champion.</summary>
    public const double CommitmentWeight = 0.45;

    /// <summary>Weight of the "how many patches" component.</summary>
    public const double SpanWeight = 0.20;

    /// <summary>Weight of the "how many games" component.</summary>
    public const double VolumeWeight = 0.20;

    /// <summary>Weight of the "how recently" component. Deliberately the smallest: a two-week break should nudge the score, not erase a career.</summary>
    public const double RecencyWeight = 0.15;

    /// <summary>
    /// Play rate at which commitment reads 0. Mirrors the lowest adaptive main
    /// threshold used by main analysis (<c>MainAnalysis:PlayRateFloor</c>, 0.12):
    /// below it a champion is not classified as a main at all, so no truemain
    /// can sit under it and the range under the floor is dead scale.
    /// </summary>
    public const double CommitmentFloor = 0.12;

    /// <summary>Patch count at which the span component saturates. Six patches is roughly a Riot season quarter — long enough that "they kept playing it through balance changes" is established.</summary>
    public const int SpanTargetPatches = 6;

    /// <summary>Career games at which the volume component saturates.</summary>
    public const int VolumeTargetGames = 200;

    /// <summary>Days of inactivity that halve the recency component. Three weeks: long enough to ignore a holiday, short enough that a stale main is visibly stale.</summary>
    public const double RecencyHalfLifeDays = 21;

    /// <summary>
    /// Scores one (player, champion) pair. Pure: same inputs, same output, no
    /// clock and no I/O — the caller resolves "now" when it derives
    /// <see cref="DedicationInputs.DaysSinceLastGame"/>.
    /// </summary>
    public static DedicationBreakdown Compute(DedicationInputs inputs)
    {
        var commitment = Commitment(inputs.PlayRate);
        var span = Span(inputs.PatchSpan);
        var volume = Volume(inputs.CareerGames);
        var recency = Recency(inputs.DaysSinceLastGame);

        var weighted = (CommitmentWeight * commitment)
                       + (SpanWeight * span)
                       + (VolumeWeight * volume)
                       + (RecencyWeight * recency);

        // The weights sum to 1, so `weighted` is already 0..1; the clamp is a
        // guard against a future re-weighting drifting off 1, not a live case.
        // Rounded to one decimal so the number the leaderboard sorts on is the
        // number it prints — two rows that display 73.4 are genuinely tied and
        // fall through to the caller's deterministic tiebreak.
        return new DedicationBreakdown(
            Score: Math.Round(100d * Clamp01(weighted), 1, MidpointRounding.AwayFromZero),
            Commitment: commitment,
            Span: span,
            Volume: volume,
            Recency: recency);
    }

    /// <summary>Share of the player's games on the champion, rescaled so <see cref="CommitmentFloor"/> reads 0 and a pure one-trick reads 1.</summary>
    public static double Commitment(double playRate)
    {
        if (double.IsNaN(playRate))
        {
            return 0d;
        }

        return Clamp01((playRate - CommitmentFloor) / (1d - CommitmentFloor));
    }

    /// <summary>Distinct tracked patches on the champion, saturating at <see cref="SpanTargetPatches"/>.</summary>
    public static double Span(int patchSpan)
        => patchSpan <= 0 ? 0d : Clamp01((double)patchSpan / SpanTargetPatches);

    /// <summary>Career games on the champion on a log curve, saturating at <see cref="VolumeTargetGames"/>.</summary>
    public static double Volume(int careerGames)
    {
        if (careerGames <= 0)
        {
            return 0d;
        }

        return Clamp01(Math.Log(1d + careerGames) / Math.Log(1d + VolumeTargetGames));
    }

    /// <summary>Exponential decay on days since the last tracked game, halving every <see cref="RecencyHalfLifeDays"/> days.</summary>
    public static double Recency(double daysSinceLastGame)
    {
        if (double.IsNaN(daysSinceLastGame))
        {
            return 0d;
        }

        // A game "in the future" (clock skew between the Riot timestamp and the
        // API host) is treated as played right now rather than rewarded with a
        // recency above 1.
        var days = Math.Max(0d, daysSinceLastGame);
        return Clamp01(Math.Pow(0.5d, days / RecencyHalfLifeDays));
    }

    private static double Clamp01(double value)
        => double.IsNaN(value) ? 0d : Math.Clamp(value, 0d, 1d);
}
