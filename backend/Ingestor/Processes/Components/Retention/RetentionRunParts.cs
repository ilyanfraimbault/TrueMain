namespace Ingestor.Processes.Components.Retention;

// The intermediate results of one MatchDataRetentionProcess run, before they are folded into
// its summary.

/// <summary>Matches whose dense timeline snapshots were pruned, and the snapshot rows deleted.</summary>
internal sealed record SnapshotPruneResult(int PrunedMatches, int DeletedSnapshots)
{
    public static SnapshotPruneResult Empty { get; } = new(0, 0);
}

/// <summary>Raw match rows deleted, and the seats kept as truncated item facts.</summary>
internal sealed record DeletionResult(int DeletedMatches, int DeletedParticipants, int TruncatedSeats = 0)
{
    public static DeletionResult Empty { get; } = new(0, 0);
}

/// <summary>What one run keeps and deletes: the retained patches per platform and the matches outside them.</summary>
internal sealed record RetentionPlan(
    int RetainedPatchCount,
    int QueueId,
    IReadOnlyDictionary<string, HashSet<string>> RetainedPatchesByPlatform,
    IReadOnlyList<string> DeletableMatchIds);
