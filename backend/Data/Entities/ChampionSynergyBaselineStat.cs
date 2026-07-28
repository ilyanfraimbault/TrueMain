namespace Data.Entities;

/// <summary>
/// The marginal win rates the synergy metric is measured against (#922), folded by
/// the same process and from the exact same matches as
/// <see cref="ChampionSynergyStat"/> — that shared cohort is the whole point of
/// storing them here instead of deriving them from another aggregate.
///
/// Synergy is observed win rate minus <em>expected</em> win rate, and the expected
/// value needs each champion's win rate on its own. Using one baseline for both
/// sides of a pair would bias every number: the tracked side is a truemain playing
/// their signature champion (win rate well above 50%), while the partner side is
/// whoever happened to be on the team (win rate near the population mean). The two
/// are therefore stored separately, discriminated by <see cref="Side"/>, and the
/// read combines them in log-odds space against the cohort intercept.
/// </summary>
public class ChampionSynergyBaselineStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Lane the champion was played in (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>
    /// Which side of a synergy pair this baseline describes — see
    /// <c>Data.Entities.SynergyBaselineSide</c>:
    /// <c>SELF</c> is "a tracked player on this champion at this lane", the same
    /// population as the tracked side of <see cref="ChampionSynergyStat"/>;
    /// <c>ALLY</c> is "this champion at this lane on a tracked player's team",
    /// the same population as the partner side. Both count the tracked player's
    /// game result, since teammates share it.
    /// </summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Per-tier elo band of the tracked player, as on <see cref="ChampionSynergyStat"/>.</summary>
    public string EloBracket { get; set; } = string.Empty;

    public int Games { get; set; }

    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}

/// <summary>
/// The two populations <see cref="ChampionSynergyBaselineStat.Side"/> discriminates.
/// Plain constants rather than an enum: the column is a short text code so a
/// <c>psql</c> session reads it without a lookup, exactly like the elo bracket and
/// team-position columns next to it.
/// </summary>
public static class SynergyBaselineSide
{
    /// <summary>A tracked player's own games on this champion at this lane.</summary>
    public const string Self = "SELF";

    /// <summary>Games where this champion at this lane was a tracked player's teammate.</summary>
    public const string Ally = "ALLY";
}
