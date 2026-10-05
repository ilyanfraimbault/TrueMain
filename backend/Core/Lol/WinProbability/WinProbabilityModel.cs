namespace Core.Lol.WinProbability;

/// <summary>One lane's lead, one side over the other: creep score, level, kills.</summary>
public readonly record struct LaneLead(string Lane, double Cs, double Level, double Kills);

/// <summary>What one side holds on the map at a moment.</summary>
/// <param name="Turrets">Enemy turrets this side destroyed.</param>
/// <param name="InhibitorsDown">Enemy inhibitors this side holds down right now.</param>
/// <param name="Dragons">Elemental drakes this side slew; the Elder is not one of them.</param>
/// <param name="Baron">The Baron's buff is up for this side.</param>
/// <param name="Elder">The Elder's buff is up for this side.</param>
public readonly record struct SideMap(int Turrets, int InhibitorsDown, int Dragons, bool Baron, bool Elder);

/// <summary>
/// The win-probability model (#1795, #1864, #1911): a logistic over each lane's lead and
/// what each side holds on the map. A C# port of
/// <c>web/layers/common/app/utils/win-probability.ts</c>, which the desktop overlay and the
/// desktop's post-game curve run; the fixture <c>web/shared/fixtures/win-probability-timeline.json</c>
/// holds both to the same figures (<c>WinProbabilityParityTests</c>), so a weight changed on
/// one side fails the other's tests. Change one, change both.
/// </summary>
public static class WinProbabilityModel
{
    /// <summary>The lanes the lead weights are fitted for, in the draft's order.</summary>
    public static readonly IReadOnlyList<string> Lanes = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    /// <summary>The minute marks the lead weights are fitted at.</summary>
    public static readonly IReadOnlyList<double> Minutes = [5, 10, 15, 20, 30];

    /// <summary>
    /// The lead weights, in log-odds, per lane: per ten creep score, per level, per kill,
    /// at each of <see cref="Minutes"/>. Fitted on TrueMain's own ranked games (2026-10-04);
    /// the TS twin documents the fit.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, LeadWeights> LeadWeightsByLane = new Dictionary<string, LeadWeights>(StringComparer.Ordinal)
    {
        ["TOP"] = new([0.116, 0.074, 0.052, 0.049, 0.049], [0.033, 0.079, 0.123, 0.184, 0.257], [0.206, 0.15, 0.109, 0.086, 0.055]),
        ["JUNGLE"] = new([0.269, 0.178, 0.101, 0.064, 0.035], [0.045, 0.073, 0.149, 0.214, 0.279], [0.269, 0.193, 0.135, 0.1, 0.06]),
        ["MIDDLE"] = new([0.159, 0.115, 0.088, 0.065, 0.042], [0.039, 0.095, 0.163, 0.212, 0.264], [0.243, 0.175, 0.112, 0.08, 0.054]),
        ["BOTTOM"] = new([0.202, 0.149, 0.112, 0.095, 0.062], [0.023, 0.056, 0.13, 0.196, 0.266], [0.229, 0.173, 0.133, 0.111, 0.079]),
        ["UTILITY"] = new([-0.037, -0.039, -0.046, -0.033, 0.001], [0.04, 0.065, 0.168, 0.258, 0.341], [0.127, 0.079, 0.033, 0.017, 0.021]),
    };

    /// <summary>Per enemy turret destroyed beyond the other side's count.</summary>
    public const double TurretWeight = 0.12;

    /// <summary>Per enemy inhibitor down right now.</summary>
    public const double InhibitorWeight = 0.5;

    /// <summary>Per elemental drake beyond the other side's count.</summary>
    public const double DragonWeight = 0.15;

    /// <summary>On top, for the side on four drakes: the soul.</summary>
    public const double SoulWeight = 0.6;

    /// <summary>While the side holds the Baron's buff.</summary>
    public const double BaronWeight = 0.9;

    /// <summary>While the side holds the Elder's buff.</summary>
    public const double ElderWeight = 1.1;

    /// <summary>Drakes for the dragon soul.</summary>
    public const int Soul = 4;

    /// <summary>How long a destroyed inhibitor stays down, in seconds.</summary>
    public const int InhibitorRespawnSeconds = 300;

    /// <summary>How long the Baron's buff lasts, in seconds.</summary>
    public const int BaronBuffSeconds = 180;

    /// <summary>How long the Elder's buff lasts, in seconds.</summary>
    public const int ElderBuffSeconds = 150;

    /// <summary>A weight at <paramref name="clock"/> (game seconds): linear between two marks, the nearest one outside them.</summary>
    public static double WeightAt(IReadOnlyList<double> weights, double clock)
    {
        var minute = clock / 60;
        if (minute <= Minutes[0])
        {
            return weights[0];
        }

        if (minute >= Minutes[^1])
        {
            return weights[^1];
        }

        var upper = 0;
        while (Minutes[upper] < minute)
        {
            upper++;
        }

        var from = Minutes[upper - 1];
        var to = Minutes[upper];
        return weights[upper - 1] + ((weights[upper] - weights[upper - 1]) * (minute - from) / (to - from));
    }

    /// <summary>
    /// One side's chance to win at <paramref name="clock"/> (game seconds): a logistic of its
    /// lanes' leads over the other side's (<paramref name="leads"/>, each one ours minus theirs)
    /// and of what each side holds on the map. Even (0.5) when nothing separates them.
    /// </summary>
    public static double WinProbability(IReadOnlyList<LaneLead> leads, SideMap ours, SideMap theirs, double clock)
    {
        var edge = LaneEdge(leads, clock) + MapEdge(ours) - MapEdge(theirs);
        return 1 / (1 + Math.Exp(-edge));
    }

    private static double MapEdge(SideMap side)
        => (TurretWeight * side.Turrets)
            + (InhibitorWeight * side.InhibitorsDown)
            + (DragonWeight * side.Dragons)
            + (side.Dragons >= Soul ? SoulWeight : 0)
            + (side.Baron ? BaronWeight : 0)
            + (side.Elder ? ElderWeight : 0);

    private static double LaneEdge(IReadOnlyList<LaneLead> leads, double clock)
    {
        double edge = 0;
        foreach (var lead in leads)
        {
            if (!LeadWeightsByLane.TryGetValue(lead.Lane, out var weights))
            {
                continue;
            }

            edge += (WeightAt(weights.Cs, clock) * lead.Cs / 10)
                + (WeightAt(weights.Level, clock) * lead.Level)
                + (WeightAt(weights.Kill, clock) * lead.Kills);
        }

        return edge;
    }
}

/// <summary>One lane's weights at each of <see cref="WinProbabilityModel.Minutes"/>.</summary>
public sealed record LeadWeights(IReadOnlyList<double> Cs, IReadOnlyList<double> Level, IReadOnlyList<double> Kill);
