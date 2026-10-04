namespace Data.Metrics.Mongo;

/// <summary>
/// Write and read access to the hourly candidate-stock snapshots (#1403). Both
/// halves sit on one interface for the same reason
/// <see cref="IDbStorageSnapshotStore"/>'s do: two ends of one tiny collection, and
/// splitting them would mean two near-identical Mongo adapters.
/// </summary>
public interface ICandidateStockSnapshotStore
{
    /// <summary>
    /// Upserts one document per (platform, status) for the hour containing
    /// <paramref name="capturedAtUtc"/>, replacing whatever that hour already held.
    /// Idempotent by design: the pipeline runs several times an hour and each run
    /// simply refreshes the hour's reading rather than appending a point.
    /// </summary>
    /// <returns>Documents written, or 0 when Mongo is not configured.</returns>
    Task<int> UpsertHourAsync(
        DateTime capturedAtUtc,
        IReadOnlyList<CandidateStockSample> samples,
        CancellationToken ct);

    /// <summary>
    /// Every snapshot at or after <paramref name="sinceUtc"/>, oldest first. Returns an
    /// empty list when Mongo is not configured, so the admin panel degrades to "no
    /// history yet" instead of failing.
    /// </summary>
    Task<IReadOnlyList<CandidateStockSnapshotPoint>> GetHistoryAsync(DateTime sinceUtc, CancellationToken ct);
}

/// <summary>
/// One (platform, status) reading as measured, before persisting: <paramref name="Count"/>
/// candidate rows, held by <paramref name="Accounts"/> distinct accounts (#1534).
/// </summary>
public sealed record CandidateStockSample(string PlatformId, string Status, long Count, long Accounts);

/// <summary>
/// One persisted point of the history. <paramref name="Accounts"/> is null on a snapshot
/// recorded before the accounts figure was (#1534).
/// </summary>
public sealed record CandidateStockSnapshotPoint(
    DateTime SnapshotHourUtc,
    string PlatformId,
    string Status,
    long Count,
    long? Accounts = null);
