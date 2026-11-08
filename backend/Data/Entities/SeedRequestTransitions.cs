namespace Data.Entities;

/// <summary>
/// The transition table of a seed request (#2474), the single place that says which
/// <see cref="SeedRequestStatus"/> may follow which:
/// <list type="bullet">
/// <item>Pending → Resolving — the ingestor's atomic claim.</item>
/// <item>Resolving → Pending — a claim given back: an interrupted run, or one orphaned by a
/// host that died before writing a terminal state, returned at the next boot.</item>
/// <item>Resolving → Ingested / Failed — the terminal outcome, written only by the run holding
/// the claim.</item>
/// </list>
/// Ingested and Failed are terminal. The store filters every write on the source statuses this
/// table names, so a stale or concurrent writer cannot turn a terminal request into another one.
/// </summary>
public static class SeedRequestTransitions
{
    /// <summary>Whether <paramref name="to"/> may follow <paramref name="from"/>.</summary>
    public static bool IsAllowed(SeedRequestStatus from, SeedRequestStatus to) => (from, to) switch
    {
        (SeedRequestStatus.Pending, SeedRequestStatus.Resolving) => true,
        (SeedRequestStatus.Resolving, SeedRequestStatus.Pending) => true,
        (SeedRequestStatus.Resolving, SeedRequestStatus.Ingested) => true,
        (SeedRequestStatus.Resolving, SeedRequestStatus.Failed) => true,
        _ => false
    };

    /// <summary>The statuses a request may be in for a write to <paramref name="to"/> to apply.</summary>
    public static IReadOnlyList<SeedRequestStatus> SourcesOf(SeedRequestStatus to)
        => [.. Enum.GetValues<SeedRequestStatus>().Where(from => IsAllowed(from, to))];

    /// <summary>Ingested and Failed end a request for good.</summary>
    public static bool IsTerminal(this SeedRequestStatus status)
        => status is SeedRequestStatus.Ingested or SeedRequestStatus.Failed;
}
