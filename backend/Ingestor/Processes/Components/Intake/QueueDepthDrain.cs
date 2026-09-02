using Ingestor.Options;

namespace Ingestor.Processes.Components.Intake;

/// <summary>
/// Turns one platform's <c>Queued</c> depth into the bounded sequence of demotion statements a
/// single retention run may issue for it (#1361).
///
/// <para>
/// Separate from the process so the arithmetic that decides "how much of a 773 k-row backlog
/// does one run take, in statements of what size" is testable without a database. Two bounds,
/// for two different failure modes: the batch size keeps one <c>UPDATE</c> from outgrowing the
/// 300 s command timeout (#988), and the batch count keeps one run from spending itself
/// entirely on the drain.
/// </para>
/// </summary>
public static class QueueDepthDrain
{
    /// <summary>
    /// The demotion batches to issue for a platform holding <paramref name="queuedDepth"/>
    /// candidates, largest-allowed first and summing to at most the excess over
    /// <see cref="IntakeOptions.MaxQueuedPerPlatform"/>. Empty when the platform is within its
    /// cap or the drain is disabled.
    /// </summary>
    public static IReadOnlyList<int> PlanBatches(int queuedDepth, IntakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxQueuedPerPlatform <= 0 || options.MaxDemotionBatchesPerRun <= 0)
        {
            return [];
        }

        var excess = queuedDepth - options.MaxQueuedPerPlatform;
        if (excess <= 0)
        {
            return [];
        }

        var batchSize = Math.Max(1, options.QueueDepthDemotionBatchSize);
        var batches = new List<int>();
        var remaining = excess;

        while (remaining > 0 && batches.Count < options.MaxDemotionBatchesPerRun)
        {
            var take = Math.Min(batchSize, remaining);
            batches.Add(take);
            remaining -= take;
        }

        return batches;
    }
}
