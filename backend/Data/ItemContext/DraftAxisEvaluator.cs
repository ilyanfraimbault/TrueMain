namespace Data.ItemContext;

/// <summary>
/// One side of a draft as the evaluator sees it: the profiles it could resolve, and how
/// many members it could not.
/// </summary>
/// <param name="Facts">The resolved profiles of the side's members.</param>
/// <param name="Missing">Members whose profile could not be resolved at all.</param>
public readonly record struct DraftSide(IReadOnlyList<ChampionProfileFacts> Facts, int Missing)
{
    /// <summary>
    /// Whether the side can carry an axis. One unknown member is tolerated — a share is
    /// weighted over the rest and a count is understated by at most one, which the bands
    /// absorb — but two turn every axis on this side into a guess, so they are dropped.
    /// </summary>
    public bool IsUsable => Facts.Count > 0 && Missing <= 1;
}

/// <summary>
/// The draft of one game from the point of view of one participant.
/// </summary>
/// <param name="Enemies">The five opponents.</param>
/// <param name="Allies">The four teammates, the participant itself excluded.</param>
/// <param name="LaneOpponent">The opponent in the same position, when there is one.</param>
/// <param name="GoldLeadAt15">The participant's gold lead over that opponent at 15 minutes, when measured.</param>
public readonly record struct DraftContext(
    DraftSide Enemies,
    DraftSide Allies,
    ChampionProfileFacts? LaneOpponent,
    double? GoldLeadAt15);

/// <summary>
/// Turns a draft into the banded situations an item's pick rate is measured against
/// (#1450). Pure: everything it needs is the measured champion profiles (#1449) of the
/// nine other participants plus the thresholds, so it is unit-testable and the fold has
/// no judgement of its own.
/// </summary>
/// <remarks>
/// An axis it cannot compute is <b>absent</b> from the result, never defaulted. A game
/// whose enemy team has two unprofiled champions contributes to no enemy-team axis rather
/// than contributing a wrong one, and an axis nobody can compute simply never accumulates
/// the games that would have to carry it.
/// </remarks>
public static class DraftAxisEvaluator
{
    public static IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Evaluate(
        DraftContext draft,
        DraftAxisThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        var axes = new Dictionary<ItemContextAxis, ItemContextBucket>();

        if (draft.Enemies.IsUsable)
        {
            var enemies = draft.Enemies.Facts;

            AddBand(axes, ItemContextAxis.EnemyMagicDamage,
                DamageWeightedShare(enemies, facts => facts.MagicShare),
                thresholds.EnemyMagicShareLow, thresholds.EnemyMagicShareHigh);

            AddBand(axes, ItemContextAxis.EnemyPhysicalDamage,
                DamageWeightedShare(enemies, facts => facts.PhysicalShare),
                thresholds.EnemyPhysicalShareLow, thresholds.EnemyPhysicalShareHigh);

            AddCount(axes, ItemContextAxis.EnemySustain,
                enemies.Count(facts => facts.SustainPerMinute >= thresholds.SustainChampionPerMinute),
                thresholds.EnemyCountLow, thresholds.EnemyCountHigh);

            AddCount(axes, ItemContextAxis.EnemyCrowdControl,
                enemies.Count(facts => facts.CrowdControlPerMinute >= thresholds.CrowdControlChampionPerMinute),
                thresholds.EnemyCountLow, thresholds.EnemyCountHigh);

            AddCount(axes, ItemContextAxis.EnemyFrontline,
                enemies.Count(facts => facts.TankRate >= thresholds.FrontlineChampionRate),
                thresholds.EnemyCountLow, thresholds.EnemyCountHigh);

            AddCount(axes, ItemContextAxis.EnemyCrit,
                enemies.Count(facts => facts.CritRate >= thresholds.CritChampionRate),
                thresholds.EnemyCountLow, thresholds.EnemyCountHigh);

            AddCount(axes, ItemContextAxis.EnemyArmorPenetration,
                enemies.Count(facts => facts.ArmorPenetrationRate >= thresholds.ArmorPenetrationChampionRate),
                thresholds.EnemyCountLow, thresholds.EnemyCountHigh);

            // Melee-ness is the one attribute that can be unknown per champion even when
            // its profile exists (Data Dragon may never have answered), so the axis is
            // carried only when every resolved enemy has the flag.
            if (enemies.All(facts => facts.IsRanged.HasValue))
            {
                AddCount(axes, ItemContextAxis.EnemyMelee,
                    enemies.Count(facts => facts.IsRanged == false),
                    thresholds.EnemyMeleeCountLow, thresholds.EnemyMeleeCountHigh);
            }
        }

        if (draft.Allies.IsUsable)
        {
            var allies = draft.Allies.Facts;

            AddBand(axes, ItemContextAxis.AllyMagicDamage,
                DamageWeightedShare(allies, facts => facts.MagicShare),
                thresholds.AllyMagicShareLow, thresholds.AllyMagicShareHigh);

            AddCount(axes, ItemContextAxis.AllyFrontline,
                allies.Count(facts => facts.TankRate >= thresholds.FrontlineChampionRate),
                thresholds.AllyFrontlineCountLow, thresholds.AllyFrontlineCountHigh);
        }

        if (draft.LaneOpponent is { } opponent)
        {
            AddBand(axes, ItemContextAxis.OpponentMagicDamage, opponent.MagicShare,
                thresholds.OpponentMagicShareLow, thresholds.OpponentMagicShareHigh);

            // Binary axes have no middle: the two ends are the two answers.
            axes[ItemContextAxis.OpponentSustain] =
                opponent.SustainPerMinute >= thresholds.SustainChampionPerMinute
                    ? ItemContextBucket.High
                    : ItemContextBucket.Low;

            if (opponent.IsRanged is { } isRanged)
            {
                axes[ItemContextAxis.OpponentRanged] = isRanged ? ItemContextBucket.High : ItemContextBucket.Low;
            }

            if (opponent.GoldLeadAt10 is { } pressure)
            {
                AddBand(axes, ItemContextAxis.OpponentLanePressure, pressure,
                    thresholds.OpponentLanePressureLow, thresholds.OpponentLanePressureHigh);
            }
        }

        if (draft.GoldLeadAt15 is { } lead)
        {
            AddBand(axes, ItemContextAxis.OwnGoldLeadAt15, lead,
                thresholds.OwnGoldLeadLow, thresholds.OwnGoldLeadHigh);
        }

        return axes;
    }

    /// <summary>
    /// A team's damage share, weighted by how much damage each member actually deals: a
    /// support's damage type says much less about what the team threatens than the carry's
    /// does, and an unweighted mean would let a 5% damage share vote as loudly as a 35% one.
    /// </summary>
    private static double DamageWeightedShare(
        IReadOnlyList<ChampionProfileFacts> side,
        Func<ChampionProfileFacts, double> share)
    {
        var weight = side.Sum(facts => facts.DamagePerGame);
        if (weight <= 0)
        {
            // No damage measured on the whole side: fall back to the plain mean rather
            // than dividing by zero. Only reachable with fixture-shaped profiles.
            return side.Average(share);
        }

        return side.Sum(facts => share(facts) * facts.DamagePerGame) / weight;
    }

    private static void AddBand(
        Dictionary<ItemContextAxis, ItemContextBucket> axes,
        ItemContextAxis axis,
        double value,
        double lowEdge,
        double highEdge)
        => axes[axis] = value < lowEdge
            ? ItemContextBucket.Low
            : value >= highEdge
                ? ItemContextBucket.High
                : ItemContextBucket.Mid;

    private static void AddCount(
        Dictionary<ItemContextAxis, ItemContextBucket> axes,
        ItemContextAxis axis,
        int count,
        int lowAtMost,
        int highAtLeast)
        => axes[axis] = count <= lowAtMost
            ? ItemContextBucket.Low
            : count >= highAtLeast
                ? ItemContextBucket.High
                : ItemContextBucket.Mid;
}
