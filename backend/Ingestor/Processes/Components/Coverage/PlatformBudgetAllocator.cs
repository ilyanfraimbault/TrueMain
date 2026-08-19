namespace Ingestor.Processes.Components.Coverage;

/// <summary>
/// Splits a run's budget across platforms from the coverage deficit (#1150).
///
/// <para>
/// The pipeline used to spend every budget — the match-ingest claim, the harvest — on one
/// cross-platform ordering, so each batch simply mirrored the account pool it drew from.
/// The pool was ~82% one region, so the batches were too, and every batch fed the pool that
/// produced it: ingesting a EUW1 match yields ~9 orphan EUW1 participants, which the harvest
/// turns into EUW1 candidates and accounts, which are then claimed first. Nothing decided
/// that split; it was the fixed point of a loop with no counterweight.
/// </para>
///
/// <para>
/// The counterweight is deliberately <em>not</em> a configured per-region quota ("300 each"):
/// that number would be a guess, would need re-tuning whenever a region is added, and would
/// keep spending on a region that no longer needs it. Instead the share follows the signal the
/// pipeline already computes for champions — how far a platform is from
/// <c>Coverage:TargetMainsPerChampion</c>:
/// </para>
///
/// <code>
/// weight(p) = 1 + MeanDeficit(p)      // in [1, 2]
/// quota(p)  = budget * weight(p) / sum(weights)
/// </code>
///
/// <para>
/// The constant 1 is what keeps this a *balancer* rather than a *switch*. A platform at full
/// coverage still carries weight 1, so it keeps its even share and its established mains keep
/// being refreshed — starving it would just trade one imbalance for the opposite one. The
/// deficit term is the bonus on top: at the extreme, a platform with no mains at all gets
/// twice the share of a fully covered one. And it is self-damping for the same reason the
/// per-champion signal is: as a region fills up its deficit shrinks, its weight decays back
/// towards 1, and the allocation converges on an even split instead of oscillating.
/// </para>
///
/// <para>
/// A quota is a floor, not a partition. Callers must spill whatever a platform cannot fill to
/// the ones that can — the same floor-not-partition semantics as
/// <c>Harvest:NewCandidateShare</c> (#495) and <c>MatchIngestion:EstablishedMainShare</c>
/// (#900) — so a run always spends its whole budget and a platform is never idled by a share
/// it has no work for.
/// </para>
/// </summary>
public static class PlatformBudgetAllocator
{
    /// <summary>
    /// Apportions <paramref name="budget"/> across <paramref name="platforms"/> by coverage
    /// deficit. Every listed platform gets at least one slot when the budget allows, so a
    /// region can never be allocated out of the run entirely.
    /// </summary>
    /// <param name="platforms">The platforms in scope; duplicates and blanks are ignored.</param>
    /// <param name="budget">Total slots to split. Values below 1 are treated as 1.</param>
    /// <param name="coverage">
    /// The cycle's frozen coverage snapshot. A neutral snapshot yields an even split, which is
    /// the right cold-start behaviour: with no mains anywhere there is no reason to favour a
    /// region.
    /// </param>
    /// <returns>Slots per platform, keyed case-insensitively, summing to <paramref name="budget"/>.</returns>
    public static IReadOnlyDictionary<string, int> Allocate(
        IReadOnlyCollection<string> platforms,
        int budget,
        ChampionCoverageSnapshot coverage)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        ArgumentNullException.ThrowIfNull(coverage);

        var scoped = platforms
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Select(platform => platform.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scoped.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var safeBudget = Math.Max(1, budget);
        var weights = scoped.ToDictionary(
            platform => platform,
            platform => 1 + coverage.MeanDeficit(platform),
            StringComparer.OrdinalIgnoreCase);
        var totalWeight = weights.Values.Sum();

        // Largest remainder, so the quotas sum to exactly the budget instead of losing slots
        // to three independent floors. Ties break on the platform id to keep a run
        // reproducible — an arbitrary tie-break would make two identical states allocate
        // differently and turn any imbalance investigation into a guess.
        var exact = scoped.ToDictionary(
            platform => platform,
            platform => safeBudget * weights[platform] / totalWeight,
            StringComparer.OrdinalIgnoreCase);

        var quotas = scoped.ToDictionary(
            platform => platform,
            platform => (int)Math.Floor(exact[platform]),
            StringComparer.OrdinalIgnoreCase);

        var remaining = safeBudget - quotas.Values.Sum();
        foreach (var platform in scoped
                     .OrderByDescending(platform => exact[platform] - Math.Floor(exact[platform]))
                     .ThenBy(platform => platform, StringComparer.OrdinalIgnoreCase)
                     .Take(Math.Max(0, remaining)))
        {
            quotas[platform]++;
        }

        return EnsureEveryPlatformIsRepresented(scoped, quotas, safeBudget);
    }

    /// <summary>
    /// Lifts any platform floored to 0 up to 1, paying for it out of the largest quota. A
    /// zero slot would mean a region sits out the run entirely — and since sitting out is
    /// what produced the imbalance, an allocator built to correct it must not be able to
    /// reproduce it. Only possible when a platform's share rounds below 1 (a small budget, or
    /// many platforms), and skipped when the budget cannot cover one slot each.
    /// </summary>
    private static Dictionary<string, int> EnsureEveryPlatformIsRepresented(
        List<string> scoped,
        Dictionary<string, int> quotas,
        int budget)
    {
        if (budget < scoped.Count)
        {
            return quotas;
        }

        foreach (var starved in scoped.Where(platform => quotas[platform] == 0).ToList())
        {
            var donor = quotas
                .Where(entry => entry.Value > 1)
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Key)
                .FirstOrDefault();

            if (donor is null)
            {
                break;
            }

            quotas[donor]--;
            quotas[starved]++;
        }

        return quotas;
    }
}
