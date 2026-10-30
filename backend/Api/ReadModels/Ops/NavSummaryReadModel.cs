namespace TrueMain.ReadModels.Ops;

/// <summary>
/// What the admin sidebar flags on its entries (#2294): the queues waiting for the operator and
/// the cockpit's verdict, so a pending triage or a red pipeline shows without opening each page.
/// </summary>
public sealed record NavSummaryReadModel
{
    /// <summary>Open known-player account conflicts: the conflict queue's open total.</summary>
    public long OpenConflicts { get; init; }

    /// <summary>Homonym groups still holding a pair nobody merged or marked: the homonyms queue's total.</summary>
    public long HomonymGroups { get; init; }

    /// <summary>When the two counts were taken; they are cached a few seconds.</summary>
    public DateTime CountedAtUtc { get; init; }

    /// <summary>
    /// The cockpit verdict — <c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c> — as the warm
    /// pipeline-health payload holds it. <c>unknown</c> as well when no recent evaluation is held:
    /// this read never runs one (see <see cref="HealthEvaluatedAtUtc"/>).
    /// </summary>
    public string HealthStatus { get; init; } = "unknown";

    /// <summary>
    /// When that verdict was evaluated; null when no evaluation recent enough is held, which tells an
    /// <c>unknown</c> that was measured from one that was not.
    /// </summary>
    public DateTime? HealthEvaluatedAtUtc { get; init; }
}
