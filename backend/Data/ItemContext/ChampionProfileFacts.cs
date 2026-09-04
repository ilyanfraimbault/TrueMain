using Data.Entities;

namespace Data.ItemContext;

/// <summary>
/// One champion's profile as the axis evaluator reads it: the shares, rates and
/// per-minute figures derived from the additive sums of <see cref="ChampionProfileStat"/>
/// (#1449). The division happens here, once per champion per patch, which is what lets
/// the fold stay additive and the normalisations change without re-folding anything.
/// </summary>
/// <remarks>
/// Members that need their own denominator are nullable rather than zero-by-default: a
/// champion whose profile has no item games has an <em>unknown</em> crit rate, not a crit
/// rate of 0, and an axis that cannot be computed is left out of the game instead of
/// being counted at one end of its scale.
/// </remarks>
public sealed record ChampionProfileFacts
{
    public required int ChampionId { get; init; }

    public required string Position { get; init; }

    /// <summary>Games the profile was folded from — the caller's floor is applied on this.</summary>
    public required int Games { get; init; }

    /// <summary>Mean damage to champions per game. The weight behind every team-level damage share.</summary>
    public required double DamagePerGame { get; init; }

    /// <summary>Share of this champion's damage to champions that is magic, in [0, 1].</summary>
    public required double MagicShare { get; init; }

    /// <summary>Share that is physical, in [0, 1]. Magic + physical + true = 1.</summary>
    public required double PhysicalShare { get; init; }

    /// <summary>Healing and shielding — on teammates and on itself — per minute.</summary>
    public required double SustainPerMinute { get; init; }

    /// <summary>Seconds of crowd control inflicted per minute.</summary>
    public required double CrowdControlPerMinute { get; init; }

    /// <summary>Share of its games completing at least one purely defensive item, or null when no item games.</summary>
    public double? TankRate { get; init; }

    public double? CritRate { get; init; }

    public double? ArmorPenetrationRate { get; init; }

    /// <summary>Whether the champion is ranged, or null when Data Dragon never answered.</summary>
    public bool? IsRanged { get; init; }

    /// <summary>Mean gold lead over its lane opponent at 10 minutes, or null when no lane was measured.</summary>
    public double? GoldLeadAt10 { get; init; }

    /// <summary>
    /// Derives the facts from one folded profile row, or <see langword="null"/> when the
    /// row holds no game — the only state the arithmetic cannot survive.
    /// </summary>
    public static ChampionProfileFacts? From(ChampionProfileStat stat)
    {
        ArgumentNullException.ThrowIfNull(stat);

        if (stat.Games <= 0)
        {
            return null;
        }

        var games = (double)stat.Games;
        var minutes = stat.GameDurationSecondsSum / 60d;
        var damage = (double)(stat.PhysicalDamageToChampionsSum
            + stat.MagicDamageToChampionsSum
            + stat.TrueDamageToChampionsSum);

        return new ChampionProfileFacts
        {
            ChampionId = stat.ChampionId,
            Position = stat.Position,
            Games = stat.Games,
            DamagePerGame = damage / games,
            MagicShare = damage > 0 ? stat.MagicDamageToChampionsSum / damage : 0d,
            PhysicalShare = damage > 0 ? stat.PhysicalDamageToChampionsSum / damage : 0d,
            SustainPerMinute = minutes > 0
                ? (stat.TotalHealSum + stat.HealsOnTeammatesSum + stat.DamageShieldedOnTeammatesSum) / minutes
                : 0d,
            CrowdControlPerMinute = minutes > 0 ? stat.TimeCCingOthersSum / minutes : 0d,
            TankRate = stat.ItemGames > 0 ? stat.TankGames / (double)stat.ItemGames : null,
            CritRate = stat.ItemGames > 0 ? stat.CritGames / (double)stat.ItemGames : null,
            ArmorPenetrationRate = stat.ItemGames > 0 ? stat.ArmorPenetrationGames / (double)stat.ItemGames : null,
            IsRanged = stat.IsRanged,
            GoldLeadAt10 = stat.LaneGamesAt10 > 0 ? stat.GoldLeadAt10Sum / (double)stat.LaneGamesAt10 : null,
        };
    }
}
