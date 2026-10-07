using Core.Lol.Pace;
using Core.Lol.Ranking;

namespace Data.Entities;

/// <summary>
/// One bin of the pace benchmark (#1912): how many participants of games played at a tier
/// sat in <see cref="Bucket"/> of <see cref="Metric"/> at <see cref="Minute"/>, per patch and
/// position. The desktop overlay reads its percentiles to set the player's CS and gold per
/// minute against the players of their tier.
/// </summary>
/// <remarks>
/// <para>
/// <b>An aggregate, never a grid.</b> The per-minute values come from the match-v5
/// timeline in memory, at ingestion, and only these additive counters are written — the
/// per-minute participant grid was removed on purpose (#772, #1599) and does not come back
/// through here. A match is folded once, gated by <see cref="Match.PaceBenchmarkAggregated"/>.
/// </para>
/// <para>
/// <b>Tier is the lobby's.</b> The whole lobby is counted at the tier of the tracked
/// account(s) in it at game time — matchmaking puts the ten players near one MMR, and
/// counting only the tracked rows would benchmark mains, not "players of the tier". It is
/// an approximation the overlay states ("games of Diamond players").
/// </para>
/// </remarks>
public class PaceBenchmarkStat
{
    public Guid Id { get; set; }

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>A ranked tier — never <c>UNRANKED</c> or <c>ALL</c>, which the type cannot hold.</summary>
    public RankTier Tier { get; set; }

    /// <summary>One of the five canonical <c>TeamPosition</c> values.</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>Whole game minute, 1 to <see cref="PaceHistogram.MaxMinute"/>.</summary>
    public int Minute { get; set; }

    public PaceMetric Metric { get; set; }

    /// <summary>Bin index: the value divided by <see cref="PaceHistogram.BucketWidth"/>.</summary>
    public int Bucket { get; set; }

    public long Count { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
