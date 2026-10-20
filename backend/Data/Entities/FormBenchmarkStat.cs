using Core.Lol.Benchmarks;
using Core.Lol.Ranking;

namespace Data.Entities;

/// <summary>
/// One bin of the form benchmark (#2076): how many participants of games played at a tier
/// ended a game in <see cref="Bucket"/> of <see cref="Metric"/>, per patch and position. The
/// desktop dashboard reads its median to set the player's form tiles (KDA, kill
/// participation, CS/min, deaths) against the players of their tier on their main role.
/// </summary>
/// <remarks>
/// The pace benchmark's sibling (<see cref="PaceBenchmarkStat"/>) and folded in the same
/// pass, from the same games, at the same lobby tier: a match is counted once, gated by
/// <see cref="Match.PaceBenchmarkAggregated"/>, and the low-tier sampler adds the games it
/// never stores. Whole-game figures, so no minute: only additive counters are written.
/// </remarks>
public class FormBenchmarkStat
{
    public Guid Id { get; set; }

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>A ranked tier — never <c>UNRANKED</c> or <c>ALL</c>, which the type cannot hold.</summary>
    public RankTier Tier { get; set; }

    /// <summary>One of the five canonical <c>TeamPosition</c> values.</summary>
    public string Position { get; set; } = string.Empty;

    public FormMetric Metric { get; set; }

    /// <summary>Bin index: the value divided by <see cref="FormHistogram.BucketWidth"/>.</summary>
    public int Bucket { get; set; }

    public long Count { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
