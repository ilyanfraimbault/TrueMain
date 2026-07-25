namespace Core.Lol.Performance;

/// <summary>
/// TrueMain's per-player match performance score: an integer 0–100 graded from
/// data the database already stores. Deliberately a pure, deterministic function
/// of its <see cref="PerformanceScoreInput"/> — same input, same score, forever —
/// so it can be recomputed on any read path without a schema change.
///
/// <para><b>Shape.</b> Seven components are each normalized to 0..1, then folded
/// into a weighted average that is rescaled to 0..100. A component whose input is
/// missing (no team kills, no @15 snapshot, zero-length game…) is <em>dropped</em>
/// and its weight redistributed over the survivors — never scored as a zero, which
/// would silently punish a player for a gap in our data.</para>
///
/// <para><b>Components and how each is normalized.</b></para>
/// <list type="bullet">
///   <item><description><b>Combat</b> — <c>(kills + assists) / max(1, deaths)</c>,
///   linear up to <c>6.0</c> KDA = full marks. Capping is intentional: one blowout
///   stat must not swamp the other six components.</description></item>
///   <item><description><b>Kill participation</b> — <c>(kills + assists) / teamKills</c>,
///   clamped to 0..1 (shared assists can push the raw ratio past 1).</description></item>
///   <item><description><b>Damage share</b> — this player's share of the team's damage
///   to champions, mapped linearly from 5% (0) to 35% (1). An even five-way split is
///   20%, so the average player lands at 0.5.</description></item>
///   <item><description><b>Gold share</b> — share of the team's gold, mapped from 10% (0)
///   to 30% (1). Tighter band than damage because passive income compresses gold share.
///   Weighted lower than damage share since the two are correlated.</description></item>
///   <item><description><b>Farming</b> — CS per minute against a role-specific reference
///   (see <see cref="RoleProfile"/>), so a support is not graded on a mid laner's wave count.</description></item>
///   <item><description><b>Vision</b> — vision score per minute against a role-specific
///   reference, for the same reason.</description></item>
///   <item><description><b>Laning</b> — the @15 leads over the lane opponent, blended
///   gold 50% / cs 25% / xp 25%. Each is centred: a dead-even lane scores 0.5, and the
///   component saturates at ±1500 gold, ±30 cs, ±1500 xp.</description></item>
/// </list>
///
/// <para><b>Role weights.</b> Each position gets its own weight profile summing to 100
/// (see <see cref="RoleProfile.For"/>): supports lean on kill participation and vision,
/// bot lane on damage and farm, jungle on kill participation. An empty or unknown
/// <c>TeamPosition</c> (ARAM, unparsed roles) falls back to a neutral profile.</para>
///
/// <para><b>Deliberate exclusions.</b> No objective participation (dragon / baron /
/// turret takedowns are not stored — Riot exposes them, we do not ingest them), no
/// damage taken, no heal/shield, no ward counts: inventing them would mean fabricating
/// numbers. No win bonus either — the score grades the individual, and winners already
/// score higher organically through KDA, farm and laning leads.</para>
/// </summary>
public static class PerformanceScore
{
    /// <summary>KDA at which the combat component saturates.</summary>
    private const double KdaFullMarks = 6.0d;

    /// <summary>Damage-share band: 5% of the team's damage scores 0, 35% scores 1.</summary>
    private const double DamageShareFloor = 0.05d;
    private const double DamageShareCeiling = 0.35d;

    /// <summary>Gold-share band: 10% of the team's gold scores 0, 30% scores 1.</summary>
    private const double GoldShareFloor = 0.10d;
    private const double GoldShareCeiling = 0.30d;

    /// <summary>@15 lead at which each laning sub-metric saturates (in either direction).</summary>
    private const double GoldDiffSpan = 1500d;
    private const double CsDiffSpan = 30d;
    private const double XpDiffSpan = 1500d;

    /// <summary>Blend of the three @15 leads inside the laning component.</summary>
    private const double LaningGoldWeight = 0.50d;
    private const double LaningCsWeight = 0.25d;
    private const double LaningXpWeight = 0.25d;

    /// <summary>
    /// Grades one participant on 0–100. Returns 0 only in the degenerate case where
    /// every component is unavailable.
    /// </summary>
    public static int Compute(PerformanceScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var profile = RoleProfile.For(input.TeamPosition);
        var minutes = input.GameDurationMinutes;

        // (weight, value) pairs — a null value means "we don't have this input for
        // this player", so the component drops out and its weight is redistributed.
        var components = new (double Weight, double? Value)[]
        {
            (profile.Combat, Combat(input)),
            (profile.KillParticipation, KillParticipation(input)),
            (profile.DamageShare, Share(
                input.DamageToChampions, input.TeamDamageToChampions, DamageShareFloor, DamageShareCeiling)),
            (profile.GoldShare, Share(
                input.GoldEarned, input.TeamGoldEarned, GoldShareFloor, GoldShareCeiling)),
            (profile.Farming, PerMinute(input.Cs, minutes, profile.CsPerMinuteFullMarks)),
            (profile.Vision, PerMinute(input.VisionScore, minutes, profile.VisionPerMinuteFullMarks)),
            (profile.Laning, Laning(input)),
        };

        var totalWeight = 0d;
        var weighted = 0d;
        foreach (var (weight, value) in components)
        {
            if (value is null || weight <= 0d)
            {
                continue;
            }

            totalWeight += weight;
            weighted += weight * value.Value;
        }

        if (totalWeight <= 0d)
        {
            return 0;
        }

        // AwayFromZero rather than the .NET default (banker's rounding) so the
        // published score is the one a reader reproduces by hand from the weights.
        return (int)Math.Round(100d * weighted / totalWeight, MidpointRounding.AwayFromZero);
    }

    private static double Combat(PerformanceScoreInput input)
    {
        var kda = (input.Kills + input.Assists) / (double)Math.Max(1, input.Deaths);
        return Clamp01(kda / KdaFullMarks);
    }

    private static double? KillParticipation(PerformanceScoreInput input)
        => input.TeamKills <= 0
            ? null
            : Clamp01((input.Kills + input.Assists) / (double)input.TeamKills);

    private static double? Share(int value, int teamTotal, double floor, double ceiling)
        => teamTotal <= 0 ? null : Band(value / (double)teamTotal, floor, ceiling);

    private static double? PerMinute(int total, double minutes, double fullMarks)
        => minutes <= 0d || fullMarks <= 0d ? null : Clamp01(total / minutes / fullMarks);

    private static double? Laning(PerformanceScoreInput input)
    {
        if (input.GoldDiff15 is not { } goldDiff
            || input.CsDiff15 is not { } csDiff
            || input.XpDiff15 is not { } xpDiff)
        {
            return null;
        }

        return (LaningGoldWeight * Centered(goldDiff, GoldDiffSpan))
               + (LaningCsWeight * Centered(csDiff, CsDiffSpan))
               + (LaningXpWeight * Centered(xpDiff, XpDiffSpan));
    }

    /// <summary>Maps a signed lead to 0..1 with a dead-even lane sitting at 0.5.</summary>
    private static double Centered(int diff, double span)
        => Clamp01(0.5d + (diff / (2d * span)));

    /// <summary>Linear map of <paramref name="value"/> from <paramref name="floor"/>=0 to <paramref name="ceiling"/>=1.</summary>
    private static double Band(double value, double floor, double ceiling)
        => Clamp01((value - floor) / (ceiling - floor));

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    /// <summary>
    /// Per-role weight profile. The seven weights sum to 100 for every role, and the
    /// two "full marks" references calibrate the per-minute components so each role is
    /// graded against its own realistic ceiling rather than a single global one.
    /// </summary>
    private readonly record struct RoleProfile(
        double Combat,
        double KillParticipation,
        double DamageShare,
        double GoldShare,
        double Farming,
        double Vision,
        double Laning,
        double CsPerMinuteFullMarks,
        double VisionPerMinuteFullMarks)
    {
        /// <summary>
        /// Weight profile for a Riot team position. Case-insensitive; an empty or
        /// unrecognised position (ARAM, unparsed roles) gets the neutral profile —
        /// roughly the average of the five lanes, so nothing is graded on a role
        /// assumption we cannot back.
        /// </summary>
        public static RoleProfile For(string? teamPosition) => teamPosition?.Trim().ToUpperInvariant() switch
        {
            // Combat, KP, Damage, Gold, Farm, Vision, Laning | cs/min ref, vision/min ref
            "TOP" => new(22, 16, 18, 8, 16, 6, 14, 9.0d, 0.9d),
            "JUNGLE" => new(20, 20, 16, 8, 16, 8, 12, 6.5d, 1.2d),
            "MIDDLE" => new(22, 16, 20, 8, 16, 6, 12, 9.0d, 0.9d),
            "BOTTOM" => new(22, 14, 22, 8, 18, 4, 12, 9.5d, 0.8d),
            "UTILITY" => new(22, 22, 8, 4, 6, 26, 12, 2.0d, 2.4d),
            _ => new(22, 18, 18, 8, 14, 8, 12, 8.0d, 1.0d),
        };
    }
}
