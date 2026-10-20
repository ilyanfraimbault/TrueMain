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

    /// <summary>How many entries the league held at that read.</summary>
    public int EntryCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
