namespace Data.Entities;

/// <summary>
/// Pre-aggregated same-team co-occurrence record backing the champion synergies
/// panel (#922). One row per
/// (champion, position, partner champion, partner position, patch, elo bracket)
/// slice: how often a tracked player on <see cref="ChampionId"/> at
/// <see cref="TeamPosition"/> had <see cref="PartnerChampionId"/> at
/// <see cref="PartnerPosition"/> on their own team, and how often that team won.
///
/// Structurally the sibling of <see cref="ChampionMatchupStat"/> — same
/// incremental fold, same "additive facts only, NO sample floor" contract so the
/// read side folds rows to the requested patch / elo scope and applies the floor
/// on the merged total. The difference is the pairing rule: the matchup table
/// pairs a tracked participant with the opposite team's same-position player,
/// this one pairs it with each of its four canonical-position teammates.
///
/// Rows are directional: the (champion, position) side is always a tracked
/// account, the partner side is whoever was on the team. So the A→B and B→A rows
/// are different populations by design and only the A→B row is read when asking
/// "you play A — who should your friends play?".
/// </summary>
public class ChampionSynergyStat
{
    public Guid Id { get; set; }

    /// <summary>Champion of the tracked side of the pair.</summary>
    public int ChampionId { get; set; }

    /// <summary>Lane of the tracked side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>Champion of the ally the tracked side was paired with.</summary>
    public int PartnerChampionId { get; set; }

    /// <summary>Lane of that ally — always different from <see cref="TeamPosition"/>.</summary>
    public string PartnerPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// Per-tier elo band of the tracked side (see <c>Core.Lol.Ranking.EloBracket</c>),
    /// mirroring <see cref="ChampionMatchupStat.EloBracket"/>: a rank-filtered read
    /// seeks the bands it wants, an unfiltered read sums every band.
    /// <c>UNRANKED</c> for games with no usable rank snapshot.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>Games the pair appeared together in, on the tracked side's team.</summary>
    public int Games { get; set; }

    /// <summary>Of those games, the ones that team won.</summary>
    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
