namespace Core.Lol.Performance;

/// <summary>
/// TrueMain's per-player match performance score: an integer 0–100 graded from
/// data the database already stores. Deliberately a pure, deterministic function
/// of its <see cref="PerformanceScoreInput"/> — same input, same score, forever —
/// so it can be recomputed on any read path without a schema change.
///
/// <para><b>Shape.</b> Eight components are each normalized to 0..1, then folded
/// into a weighted average that is rescaled to 0..100. A component whose input is
/// missing (no team kills, no timeline snapshot, a zero-length game…) is
/// <em>dropped</em> and its weight redistributed over the survivors — never scored
/// as a zero, which would silently punish a player for a gap in our data.</para>
///
/// <para><b>Components and how each is normalized.</b></para>
/// <list type="bullet">
///   <item><description><b>Combat</b> — <c>(kills + assists) / max(1, deaths)</c>,
///   linear up to <c>6.0</c> KDA = full marks. Capping is intentional: one blowout
///   stat must not swamp the other seven components.</description></item>
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
///   <item><description><b>Laning</b> — the leads over the lane opponent at the canonical
///   timeline marks up to and including minute <see cref="LaningPhaseLastMinute"/>
///   (5 / 10 / 15). Each mark blends gold 50% / cs 25% / xp 25%, is centred so a dead-even
///   lane scores 0.5, and saturates at a lead of <see cref="GoldSpanPerMinute"/> gold,
///   <see cref="CsSpanPerMinute"/> cs and <see cref="XpSpanPerMinute"/> xp <em>per elapsed
///   minute</em>. Marks are then averaged weighted by their own minute, so the state of the
///   lane at 15 counts three times what it did at 5.</description></item>
///   <item><description><b>Mid game</b> — the same construction over the marks after the
///   laning phase (20 / 30): did the lead survive, grow, or evaporate once the lane broke.
///   A game that ends before minute 20 has no such mark and simply drops the
///   component.</description></item>
/// </list>
///
/// <para><b>Role weights.</b> Each position gets its own weight profile summing to 100
/// (see <see cref="RoleProfile.For"/>): supports lean on kill participation and vision,
/// bot lane on damage and farm, jungle on kill participation and the mid game. An empty
/// or unknown <c>TeamPosition</c> (ARAM, unparsed roles) falls back to a neutral profile.</para>
///
/// <para><b>Deliberate exclusions.</b> No objective participation (dragon / baron /
/// turret takedowns are not stored — Riot exposes them, we do not ingest them), no
/// damage taken, no heal/shield, no ward counts: inventing them would mean fabricating
/// numbers. No win bonus either — the score grades the individual, and winners already
/// score higher organically through KDA, farm and the lead components.</para>
/// </summary>
public static class PerformanceScore
{
    /// <summary>
    /// Last canonical timeline mark that counts as laning phase. Marks at or below
    /// it feed the laning component, later ones feed the mid-game component.
    /// </summary>
    public const int LaningPhaseLastMinute = 15;

    /// <summary>Gold lead <em>per elapsed minute</em> at which a mark's gold term saturates.</summary>
    public const double GoldSpanPerMinute = 100d;

    /// <summary>CS lead per elapsed minute at which a mark's cs term saturates.</summary>
    public const double CsSpanPerMinute = 2d;

    /// <summary>XP lead per elapsed minute at which a mark's xp term saturates.</summary>
    public const double XpSpanPerMinute = 100d;

    /// <summary>KDA at which the combat component saturates.</summary>
    private const double KdaFullMarks = 6.0d;

    /// <summary>Damage-share band: 5% of the team's damage scores 0, 35% scores 1.</summary>
    private const double DamageShareFloor = 0.05d;
    private const double DamageShareCeiling = 0.35d;

    /// <summary>Gold-share band: 10% of the team's gold scores 0, 30% scores 1.</summary>
    private const double GoldShareFloor = 0.10d;
    private const double GoldShareCeiling = 0.30d;

    /// <summary>Blend of the three leads inside one timeline mark.</summary>
    private const double LeadGoldWeight = 0.50d;
    private const double LeadCsWeight = 0.25d;
    private const double LeadXpWeight = 0.25d;

    /// <summary>
    /// Grades one participant on 0–100. The floor is reached when every component
    /// that <em>is</em> available grades 0 — a 0/20/0 line with no farm, no vision
    /// and a lost lane — and the ceiling when they all grade 1. Combat is always
    /// available, so at least one component always contributes.
    /// </summary>
    public static int Compute(PerformanceScoreInput input) => Explain(input).Score;

    /// <summary>
    /// The same grading as <see cref="Compute"/>, keeping the per-component detail:
    /// every component's nominal role weight, its 0..1 grade (or <c>null</c> when it
    /// was dropped for want of data) and the share of the published score it carried
    /// once the dropped weight was redistributed.
    /// </summary>
    public static PerformanceScoreBreakdown Explain(PerformanceScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var profile = RoleProfile.For(input.TeamPosition);
        var minutes = input.GameDurationMinutes;

        // (kind, weight, value) triples — a null value means "we don't have this
        // input for this player", so the component drops out and its weight is
        // redistributed. Order matches PerformanceComponentKind so callers may
        // index the result positionally.
        var graded = new (PerformanceComponentKind Kind, double Weight, double? Value)[]
        {
            (PerformanceComponentKind.Combat, profile.Combat, Combat(input)),
            (PerformanceComponentKind.KillParticipation, profile.KillParticipation, KillParticipation(input)),
            (PerformanceComponentKind.DamageShare, profile.DamageShare, Share(
                input.DamageToChampions, input.TeamDamageToChampions, DamageShareFloor, DamageShareCeiling)),
            (PerformanceComponentKind.GoldShare, profile.GoldShare, Share(
                input.GoldEarned, input.TeamGoldEarned, GoldShareFloor, GoldShareCeiling)),
            (PerformanceComponentKind.Farming, profile.Farming, PerMinute(input.Cs, minutes, profile.CsPerMinuteFullMarks)),
            (PerformanceComponentKind.Vision, profile.Vision, PerMinute(input.VisionScore, minutes, profile.VisionPerMinuteFullMarks)),
            (PerformanceComponentKind.Laning, profile.Laning, Leads(input, laningPhase: true)),
            (PerformanceComponentKind.MidGame, profile.MidGame, Leads(input, laningPhase: false)),
        };

        var totalWeight = 0d;
        var weighted = 0d;
        foreach (var (_, weight, value) in graded)
        {
            if (value is null || weight <= 0d)
            {
                continue;
            }

            totalWeight += weight;
            weighted += weight * value.Value;
        }

        var components = new PerformanceComponentScore[graded.Length];
        for (var i = 0; i < graded.Length; i++)
        {
            var (kind, weight, value) = graded[i];
            var counts = value is not null && weight > 0d && totalWeight > 0d;
            components[i] = new PerformanceComponentScore
            {
                Kind = kind,
                Weight = weight,
                Value = value,
                EffectiveWeight = counts ? weight / totalWeight : 0d,
            };
        }

        // Unreachable by construction: Combat is never null and every role profile
        // weights it above 0, so totalWeight is always >= 18. Kept as a guard on the
        // division below — should a future profile ever zero the combat weight, this
        // degrades to 0 instead of dividing by zero and returning the unspecified
        // int cast of a NaN.
        var score = totalWeight <= 0d
            ? 0
            // AwayFromZero rather than the .NET default (banker's rounding) so the
            // published score is the one a reader reproduces by hand from the weights.
            : (int)Math.Round(100d * weighted / totalWeight, MidpointRounding.AwayFromZero);

        return new PerformanceScoreBreakdown
        {
            Score = score,
            Components = components,
        };
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

    /// <summary>
    /// Folds the lane leads of one phase into a single 0..1 grade. Each mark is
    /// graded independently against a saturation span proportional to its own
    /// minute — a 1 000 gold lead is dominant at 10 minutes and ordinary at 30 —
    /// then the marks are averaged weighted by that same minute, so the later,
    /// more decisive marks of a phase carry more. Null when the phase has no
    /// usable mark, and marks at a non-positive minute are ignored rather than
    /// dividing by zero.
    /// </summary>
    private static double? Leads(PerformanceScoreInput input, bool laningPhase)
    {
        var totalWeight = 0d;
        var weighted = 0d;

        foreach (var lead in input.LaneLeads)
        {
            if (lead.Minute <= 0 || (lead.Minute <= LaningPhaseLastMinute) != laningPhase)
            {
                continue;
            }

            var value = (LeadGoldWeight * Centered(lead.GoldDiff, GoldSpanPerMinute * lead.Minute))
                        + (LeadCsWeight * Centered(lead.CsDiff, CsSpanPerMinute * lead.Minute))
                        + (LeadXpWeight * Centered(lead.XpDiff, XpSpanPerMinute * lead.Minute));

            totalWeight += lead.Minute;
            weighted += lead.Minute * value;
        }

        return totalWeight <= 0d ? null : weighted / totalWeight;
    }

    /// <summary>Maps a signed lead to 0..1 with a dead-even lane sitting at 0.5.</summary>
    private static double Centered(int diff, double span)
        => Clamp01(0.5d + (diff / (2d * span)));

    /// <summary>Linear map of <paramref name="value"/> from <paramref name="floor"/>=0 to <paramref name="ceiling"/>=1.</summary>
    private static double Band(double value, double floor, double ceiling)
        => Clamp01((value - floor) / (ceiling - floor));

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    /// <summary>
    /// Per-role weight profile. The eight weights sum to 100 for every role, and the
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
        double MidGame,
        double CsPerMinuteFullMarks,
        double VisionPerMinuteFullMarks)
    {
        /// <summary>
        /// Weight profile for a Riot team position. Case-insensitive; an empty or
        /// unrecognised position (ARAM, unparsed roles) gets the neutral profile —
        /// roughly the average of the five lanes, so nothing is graded on a role
        /// assumption we cannot back.
        ///
        /// <para>The weight that used to grade early out-of-lane kill participations
        /// sits on kill participation now that the roam axis is gone: both measure
        /// "was this player there when the team got kills", so folding it there keeps
        /// each role's balance rather than diluting it across seven unrelated
        /// axes.</para>
        /// </summary>
        public static RoleProfile For(string? teamPosition) => teamPosition?.Trim().ToUpperInvariant() switch
        {
            // Combat, KP, Damage, Gold, Farm, Vision, Laning, MidGame | cs/min ref, vision/min ref
            "TOP" => new(20, 19, 16, 7, 14, 5, 12, 7, 9.0d, 0.9d),
            "JUNGLE" => new(18, 18, 14, 7, 14, 7, 12, 10, 6.5d, 1.2d),
            "MIDDLE" => new(20, 20, 18, 7, 14, 5, 10, 6, 9.0d, 0.9d),
            "BOTTOM" => new(20, 15, 20, 7, 16, 4, 10, 8, 9.5d, 0.8d),
            "UTILITY" => new(18, 28, 7, 4, 5, 24, 8, 6, 2.0d, 2.4d),
            _ => new(20, 20, 16, 7, 12, 7, 10, 8, 8.0d, 1.0d),
        };
    }
}
