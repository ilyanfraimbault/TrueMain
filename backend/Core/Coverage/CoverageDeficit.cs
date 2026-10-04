namespace Core.Coverage;

/// <summary>
/// The region-coverage arithmetic, spelled once (#1150, #1153).
///
/// <para>
/// Two readers need it. The ingestor's claim allocator weights each platform's share of a
/// batch by <see cref="Mean"/>, and the admin health cockpit shows that same number per
/// platform so an operator can see the split the allocator is acting on. The second reader
/// lives in the Api, which does not reference the Ingestor; keeping the formula here is what
/// stops the panel from showing a deficit the allocator never used.
/// </para>
/// </summary>
public static class CoverageDeficit
{
    /// <summary>
    /// The target as the arithmetic uses it: at least 1, so a misconfigured 0 cannot divide by
    /// zero. The ingestor validates it as positive at boot; this is the backstop.
    /// </summary>
    public static int NormalizeTarget(int targetMainsPerChampion) => Math.Max(1, targetMainsPerChampion);

    /// <summary>
    /// Scarcity of one champion on one platform, in [0, 1]: 1 = no active main there, 0 = at
    /// or above <paramref name="targetMainsPerChampion"/>.
    /// </summary>
    public static double Of(int mains, int targetMainsPerChampion)
    {
        var target = NormalizeTarget(targetMainsPerChampion);
        return Math.Clamp((target - mains) / (double)target, 0, 1);
    }

    /// <summary>
    /// How under-covered a platform is overall: the mean of <see cref="Of"/> over
    /// <paramref name="championIds"/>, the champions holding an active main on <em>any</em>
    /// platform. Averaging over the shared universe rather than the platform's own champions
    /// is deliberate — a champion missing from a region entirely is the strongest deficit there
    /// is, and must count. 0 when the universe is empty.
    /// </summary>
    /// <param name="championIds">The shared champion universe.</param>
    /// <param name="mainsFor">Active mains of a champion on the platform being measured.</param>
    /// <param name="targetMainsPerChampion"><c>Coverage:TargetMainsPerChampion</c>.</param>
    public static double Mean(
        IReadOnlyCollection<int> championIds,
        Func<int, int> mainsFor,
        int targetMainsPerChampion)
    {
        ArgumentNullException.ThrowIfNull(championIds);
        ArgumentNullException.ThrowIfNull(mainsFor);

        if (championIds.Count == 0)
        {
            return 0;
        }

        var total = 0d;
        foreach (var championId in championIds)
        {
            total += Of(mainsFor(championId), targetMainsPerChampion);
        }

        return Math.Clamp(total / championIds.Count, 0, 1);
    }

    /// <summary>
    /// The allocator's weight for a platform: <c>1 + MeanDeficit</c>, in [1, 2]. A fully
    /// covered platform keeps weight 1 — its even share — and an empty one gets twice that.
    /// </summary>
    public static double AllocationWeight(double meanDeficit) => 1 + meanDeficit;
}
