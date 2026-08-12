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
/// not drift: the ingestor's <c>ChampionLaneOutcomeAggregationProcess</c>, which folds
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
    /// Exposed here as the default both consumers' options bind to, not as the value
    /// they read directly — the ingestor's copy stays configurable because changing it
    /// re-defines every *stored* counter (#919), while the API's recomputes per request
    /// and could in principle follow a different one. Keeping the default in one place
    /// is what stops the two from silently parting company; a deployment that overrides
    /// one must override both.
    /// </para>
    /// </summary>
    public const int DefaultGoldLeadThreshold = 300;

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
