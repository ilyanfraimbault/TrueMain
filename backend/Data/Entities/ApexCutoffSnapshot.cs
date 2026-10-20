using Core.Lol.Ranking;

namespace Data.Entities;

/// <summary>
/// The lowest LP held in one region's Grandmaster or Challenger league on one UTC day (#2174):
/// the threshold the ranked card's LP curve draws for that tier. Below Master a tier starts at
/// a fixed LP; these two move every day, so they are measured rather than derived.
/// <para>
/// Written by <c>LadderSyncProcess</c> from the apex lists it already reads whole, one row per
/// (platform, tier, day): the latest read of the day replaces the earlier ones.
/// </para>
/// <para>
/// The days before the first measurement are estimated once from the tracked accounts' rank
/// snapshots (#2181, <see cref="IsEstimated"/>), so the curve has a threshold to draw before
/// LadderSync has measured one. A measurement always replaces an estimate, never the reverse.
/// </para>
/// </summary>
public class ApexCutoffSnapshot
{
    /// <summary>Platform the league belongs to (e.g. "EUW1").</summary>
    public string PlatformId { get; set; } = string.Empty;

    /// <summary><see cref="RankTier.Grandmaster"/> or <see cref="RankTier.Challenger"/>.</summary>
    public RankTier Tier { get; set; }

    /// <summary>UTC calendar day of the measurement.</summary>
    public DateOnly CapturedOnUtc { get; set; }

    /// <summary>Lowest LP among the league's entries at the latest read of the day.</summary>
    public int MinLeaguePoints { get; set; }

    /// <summary>How many entries the league held at that read (the snapshots behind an estimate).</summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// True when the value is the lowest LP among that day's tracked snapshots of the tier rather
    /// than a read of the whole league: a sample of the league, so at or above the real cutoff.
    /// </summary>
    public bool IsEstimated { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
