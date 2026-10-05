namespace Data.Entities;

/// <summary>
/// The marginal win rates the opposing-pair metric is measured against (#1713),
/// folded by the same process and from the same matches as
/// <see cref="ChampionOpponentStat"/> — the reason
/// <see cref="ChampionSynergyBaselineStat"/> exists, applied to enemies.
///
/// <para>
/// The metric is observed minus expected win rate, expected combining the tracked
/// side's own rate with the enemy's in log-odds space against the cohort intercept
/// (<c>Core.Lol.Synergy.SynergyMath</c>). Both rates are read from the tracked
/// player's side: <c>ENEMY</c> counts the tracked player's wins in the games this
/// champion was against them, so a strong enemy has a low rate and pulls the
/// expectation down — the same formula as an ally, with the sign carried by the data.
/// </para>
/// </summary>
public class ChampionOpponentBaselineStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>One of <see cref="OpponentBaselineSide"/>.</summary>
    public string Side { get; set; } = string.Empty;

    public string Patch { get; set; } = string.Empty;

    /// <summary>Per-tier elo band of the tracked player.</summary>
    public string EloBracket { get; set; } = string.Empty;

    public int Games { get; set; }

    /// <summary>Of <see cref="Games"/>, those the tracked player won.</summary>
    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}

/// <summary>
/// The two populations <see cref="ChampionOpponentBaselineStat.Side"/> discriminates,
/// as short text codes like <see cref="SynergyBaselineSide"/>.
/// </summary>
public static class OpponentBaselineSide
{
    /// <summary>A tracked player's own games on this champion at this lane.</summary>
    public const string Self = "SELF";

    /// <summary>Games with this champion at this lane on the enemy team of a tracked player.</summary>
    public const string Enemy = "ENEMY";
}
