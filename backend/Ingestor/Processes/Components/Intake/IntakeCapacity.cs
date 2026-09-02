using Ingestor.Options;

namespace Ingestor.Processes.Components.Intake;

/// <summary>
/// The one place that answers "how many new accounts can a cycle actually absorb?" (#1361),
/// so the stages that feed the claim are sized from the claim rather than from three
/// independently chosen absolute budgets.
///
/// <para>
/// The claim is the only stage that spends match-v5 calls, so it is the pipeline's real
/// throughput. Everything upstream — the harvest's refresh pass, the scoring promotion, the
/// queue depth retention allows — is work whose only consumer is that claim; sized above it,
/// the surplus is not "buffer", it is rows rewritten every cycle and read by nobody.
/// </para>
/// </summary>
public static class IntakeCapacity
{
    /// <summary>
    /// Slots a single claim reserves for freshly <c>Queued</c> candidates: the batch minus the
    /// share reserved for established mains (#900). The floor of 1 is deliberate — the share
    /// is a floor and not a partition, so a configuration of 1.0 still lets new candidates use
    /// whatever established mains leave, and reporting a capacity of 0 there would stall every
    /// derivation below on a configuration that does not actually stall the claim.
    /// </summary>
    public static int NewCandidateSlotsPerCycle(MatchIngestionOptions matchIngestion)
    {
        ArgumentNullException.ThrowIfNull(matchIngestion);

        var batchSize = Math.Max(0, matchIngestion.BatchSize);
        var establishedShare = Math.Clamp(matchIngestion.EstablishedMainShare, 0d, 1d);

        // Deliberately the complement of the claim's own expression rather than an
        // independent ceiling of batchSize x (1 - share): the claim reserves
        // ceil(quota x share) for established mains (RiotAccountRepository), so mirroring the
        // shape keeps the two readable against each other — and avoids 750 x 0.3 evaluating
        // to 225.00000000000003 and reading as 226.
        //
        // Not an exact match, and it does not need to be: the claim applies its ceiling
        // per platform, and a ceiling is super-additive, so the real reservation can exceed
        // ceil(batchSize x share) by up to one slot per platform (3 platforms at quota 25 and
        // share 0.7 reserve 3 x 18 = 54, against the 53 computed here — 21 real slots against
        // 22). This is an upper bound on a sizing input, off by less than the platform count,
        // and every consumer clamps it further (MinPromotionPerPlatform, TopNPerPlatform).
        return Math.Max(1, batchSize - (int)Math.Ceiling(batchSize * establishedShare));
    }

    /// <summary>
    /// How many candidates a cycle may promote to <c>Queued</c> on one platform: the claim's
    /// capacity for new candidates, times the configured headroom, split across the platforms
    /// that share it — floored at <see cref="IntakeOptions.MinPromotionPerPlatform"/> and
    /// never above <paramref name="topNPerPlatform"/>, which stays the explicit ceiling.
    /// </summary>
    public static int PromotionCapPerPlatform(
        MatchIngestionOptions matchIngestion,
        IntakeOptions intake,
        int platformCount,
        int topNPerPlatform)
    {
        ArgumentNullException.ThrowIfNull(intake);

        var capacity = NewCandidateSlotsPerCycle(matchIngestion);
        var platforms = Math.Max(1, platformCount);
        var headroom = Math.Max(0d, intake.PromotionHeadroomFactor);

        var derived = (int)Math.Ceiling(capacity * headroom / platforms);
        var floored = Math.Max(derived, Math.Max(1, intake.MinPromotionPerPlatform));

        return Math.Min(floored, Math.Max(1, topNPerPlatform));
    }

    /// <summary>
    /// Budget for the harvest's refresh pass — re-reading the observed stats of pairs that
    /// already have a candidate. It is capped at the same intake capacity as the promotion,
    /// because a refreshed score is only worth computing if a promotion can still read it
    /// before it goes stale. The harvest's discovery pass (new pairs) is deliberately not
    /// sized from here: finding unseen players is cheap, has no claim dependency, and is the
    /// half that keeps the pool from converging on the region we already ingest most (#495).
    /// </summary>
    public static int RefreshBudgetPerRun(MatchIngestionOptions matchIngestion, IntakeOptions intake)
    {
        ArgumentNullException.ThrowIfNull(intake);

        var capacity = NewCandidateSlotsPerCycle(matchIngestion);
        var headroom = Math.Max(0d, intake.PromotionHeadroomFactor);
        return Math.Max(1, (int)Math.Ceiling(capacity * headroom));
    }

    /// <summary>
    /// The claim's established-main share, adapted to the coverage deficit (#1361). The
    /// configured share is the midpoint at a deficit of 0.5; a fully covered scope shifts up
    /// by <see cref="IntakeOptions.EstablishedMainShareSwing"/> (depth over breadth), one with
    /// no coverage at all shifts down by the same amount (breadth first). The result is
    /// clamped to [0, 1], which is the range the claim query accepts.
    /// </summary>
    /// <param name="configuredShare">
    /// <c>MatchIngestion:EstablishedMainShare</c>, the midpoint of the adaptive range.
    /// </param>
    /// <param name="swing">Half-width of the adaptive range.</param>
    /// <param name="meanDeficit">
    /// Mean coverage deficit over the platforms in scope, in [0, 1] (0 = at target everywhere,
    /// 1 = no mains at all).
    /// </param>
    public static double AdaptiveEstablishedMainShare(double configuredShare, double swing, double meanDeficit)
    {
        var midpoint = Math.Clamp(configuredShare, 0d, 1d);
        var halfWidth = Math.Max(0d, swing);
        var deficit = Math.Clamp(meanDeficit, 0d, 1d);

        // (1 - 2 x deficit) maps [0, 1] onto [+1, -1]: covered scopes pull the share up
        // towards established mains, under-covered ones pull it down towards new candidates,
        // and the configured value is returned exactly at deficit 0.5.
        return Math.Clamp(midpoint + halfWidth * (1 - 2 * deficit), 0d, 1d);
    }
}
