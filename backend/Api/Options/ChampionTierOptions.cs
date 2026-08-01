namespace TrueMain.Options;

/// <summary>
/// Blend weights for <see cref="Services.Champions.ChampionTierCalculator"/>'s
/// S/A/B/C/D scoring. Product knobs the user wants to tweak without a
/// redeploy, so they bind from <c>ChampionTier:*</c> in configuration.
///
/// <para>
/// <see cref="PickRateWeight"/> + <see cref="BanRateWeight"/> +
/// <see cref="WinRateWeight"/> must sum to <c>1</c> (enforced at startup in
/// <c>Program.cs</c>): each metric is percentile-ranked into <c>[0, 1]</c>
/// before weighting, so a score of <c>1.0</c> (or a renormalized ban-free
/// score — see <see cref="BanRateWeight"/>) is only guaranteed to stay within
/// <c>[0, 1]</c> when the weights themselves sum to <c>1</c>.
/// </para>
/// </summary>
public sealed class ChampionTierOptions
{
    public const string SectionName = "ChampionTier";

    /// <summary>
    /// Weight applied to a row's lane-percentile pick rate. The dominant term
    /// by design (#971): pick rate is the strongest signal a tier list reads
    /// for — what the population actually plays — and unlike win rate it
    /// doesn't need a sample-size correction to be trustworthy.
    /// </summary>
    public double PickRateWeight { get; set; } = 0.45;

    /// <summary>
    /// Weight applied to a row's lane-percentile ban rate. Second-largest
    /// weight: a champion opponents remove from the pool before it is even
    /// played is a presence signal at least as strong as pick rate, and
    /// stronger than a noisy win rate. Renormalized away (its share folded
    /// into <see cref="PickRateWeight"/> / <see cref="WinRateWeight"/>,
    /// proportionally) for any patch with no ban data at all (pre-#920).
    /// </summary>
    public double BanRateWeight { get; set; } = 0.30;

    /// <summary>
    /// Weight applied to a row's lane-percentile, bayesian-shrunk win rate.
    /// Kept meaningfully non-zero — win rate still answers "does this
    /// champion actually win" — but no longer dominant, since a handful of
    /// games can swing a raw win rate far more than it can swing pick or ban
    /// share. See <see cref="WinRateShrinkageGames"/> for how the noise at
    /// low sample sizes is tamed before this weight is even applied.
    /// </summary>
    public double WinRateWeight { get; set; } = 0.25;

    /// <summary>
    /// Bayesian shrinkage constant <c>K</c> for win rate:
    /// <c>wrAdj = (wins + K * prior) / (games + K)</c>, where <c>prior</c> is
    /// the field-wide win rate of the rows being tiered. A row with far fewer
    /// than <c>K</c> games is pulled hard toward the prior (a 12-game 70% WR
    /// row lands close to 50%); a row with many more than <c>K</c> games is
    /// barely moved. This is the primary fix for micro-sample rows fluking
    /// into S-tier — the weight rebalance alone would not be enough, since a
    /// raw win rate of 0.70 vs. 0.53 is still a large gap before any
    /// weighting is applied. 100 games is comfortably above
    /// <c>ChampionsList:MinSampleGames</c> (10) so it still meaningfully
    /// shrinks the whole noisy band just above that floor, while barely
    /// touching genuinely well-sampled staples.
    /// </summary>
    public int WinRateShrinkageGames { get; set; } = 100;
}
