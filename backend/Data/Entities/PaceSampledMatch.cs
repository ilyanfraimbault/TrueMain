using Core.Lol.Ranking;

namespace Data.Entities;

/// <summary>
/// A match the pace sampler (#1912) has already folded into <see cref="PaceBenchmarkStat"/>:
/// the low-tier games it reads are never stored as <see cref="Match"/> rows, so this ledger is
/// the only thing that keeps one from being counted twice. Pruned once older than any match the
/// sampler can still be handed.
/// </summary>
public class PaceSampledMatch
{
    /// <summary>The Riot match id (<c>EUW1_7123456789</c>).</summary>
    public string MatchId { get; set; } = string.Empty;

    /// <summary>The tier the lobby was counted at — the seed player's.</summary>
    public RankTier Tier { get; set; }

    public DateTime SampledAtUtc { get; set; }
}
