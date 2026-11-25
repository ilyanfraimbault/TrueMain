namespace Core.Lol.Lane;

/// <summary>How a lane stood at the 15-minute mark.</summary>
public enum LaneStanding
{
    /// <summary>Inside the threshold band — decided by neither side.</summary>
    Even,

    /// <summary>Ahead by more than the threshold.</summary>
    Won,

    /// <summary>Behind by more than the threshold.</summary>
    Lost,
}

/// <summary>
/// What "the lane was won" means, in one place. Two consumers judge it and they must
/// not drift: the ingestor's <c>ChampionMatchupLeadAggregationProcess</c>, which folds
/// the site-wide counters, and the API's live pass over a composition's sampled games
/// (#1117). A lane win rate that meant one thing on the champion page and another on
/// the matchup tool would be worse than having only one of them.
/// </summary>
public static class LaneOutcomeRules
{
    /// <summary>
    /// Gold gap at 15 minutes past which a lane counts as decided. Roughly two camps
    /// or a wave and a half: small enough that a genuinely won lane clears it, large
    /// enough that one lucky trade does not decide the number.
    ///
    /// <para>
    /// The one value both consumers read (#2481, F1). It used to be the default of two
    /// options — the ingestor's, which re-defines every stored counter (#919), and the
    /// API's, recomputed per request — and overriding one silently split the champion
    /// page from the matchup tool. A change is a code change on purpose: it re-defines
    /// the stored counters, which every fold run records in its summary.
    /// </para>
    /// </summary>
    public const int GoldLeadThreshold = 300;

    /// <summary>
    /// Judges one lane from the champion's signed gold gap. A *threshold* necessarily
    /// creates three outcomes, not two: the band in the middle is decided by neither
    /// side and belongs in neither counter — folding it into losses would print "lane
    /// lost" where nothing happened. The comparison is strict, so exactly ±threshold
    /// is even.
    /// </summary>
    public static LaneStanding Judge(int goldDiff, int threshold)
    {
        if (goldDiff > threshold)
        {
            return LaneStanding.Won;
        }

        return goldDiff < -threshold ? LaneStanding.Lost : LaneStanding.Even;
    }
}
