using Data.Entities;
using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for recorded process runs. Aggregation-heavy reads (rollup,
/// latest-per-process) are written as explicit pipelines over
/// the document field names, mirroring the SQL the Postgres implementation used
/// (grouped scans + DISTINCT ON), with the same tie-breaks (id desc) so paging
/// and latest-run picks stay deterministic. The lifecycle writes (the transitions
/// a run takes) live in ProcessRunStore.Lifecycle.cs.
/// </summary>
internal sealed partial class ProcessRunStore(MongoLogContext context) : IProcessRunStore
{
    // Same first-use index bootstrap as DbStorageSnapshotStore: no sink to hang
    // startup index creation off, so ensure lazily; the flag is only set after a
    // success so a transient Mongo failure retries on the next call.
    private int _indexesEnsured;
    private int _headersBackfilled;
    private readonly ProcessIterationHeaders _headers = new(context);

    public async Task<DateTime?> GetLastCompletedRunStartAsync(string processName, DateTime nowUtc, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        // Judged on the effective status (#2474). A fresh Running row is excluded because the
        // caller is itself the in-flight run the recorder just opened; a stale one reads as
        // Abandoned and counts like any other run that started its work. Skipped is excluded
        // because a cadence guard asks "when did this process last actually run?" — counting
        // a skip as an answer makes the skip its own predecessor, so the guard re-arms on
        // every iteration and the process never runs again (#1149). Failed and Cancelled
        // still count: both spent part of their Riot budget, so the interval before the next
        // attempt is deliberate (#1513).
        var filter = Builders<ProcessRunDocument>.Filter.Eq(doc => doc.ProcessName, processName)
                     & ProcessRunStaleness.EffectiveStatusIn(ProcessRunStatusExtensions.CompletedStatuses, nowUtc);

        var latest = await context.ProcessRuns
            .Find(filter)
            .SortByDescending(doc => doc.StartedAtUtc)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        return latest?.StartedAtUtc;
    }

    public async Task<ProcessRunPage> QueryRunsAsync(
        string? processName,
        ProcessRunStatus? status,
        DateTime? since,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return new ProcessRunPage([], 0);
        }

        await EnsureIndexesOnceAsync(ct);

        var filter = BuildRunsFilter(processName, status, since);

        var total = await context.ProcessRuns.CountDocumentsAsync(filter, cancellationToken: ct);

        var runs = await context.ProcessRuns
            .Find(filter)
            // Newest first; id breaks ties so paging is stable when several runs
            // share a StartedAtUtc.
            .Sort(Builders<ProcessRunDocument>.Sort
                .Descending(doc => doc.StartedAtUtc)
                .Descending(doc => doc.Id))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new ProcessRunPage(runs, total);
    }

    public async Task<IReadOnlyList<ProcessRunSummarySample>> GetRunSummariesAsync(
        IReadOnlyCollection<string> processNames,
        DateTime sinceUtc,
        CancellationToken ct)
    {
        if (!context.IsActive || processNames.Count == 0)
        {
            return [];
        }

        await EnsureIndexesOnceAsync(ct);

        var filter = Builders<ProcessRunDocument>.Filter.And(
            Builders<ProcessRunDocument>.Filter.In(doc => doc.ProcessName, processNames),
            Builders<ProcessRunDocument>.Filter.Gte(doc => doc.StartedAtUtc, sinceUtc));

        // ix_process_started covers this filter and the sort exactly. Oldest first so
        // the caller can walk the window forward into buckets without re-sorting.
        var runs = await context.ProcessRuns
            .Find(filter)
            .Project(doc => new ProcessRunSummarySample(doc.ProcessName, doc.StartedAtUtc, doc.SummaryJson))
            .Sort(Builders<ProcessRunDocument>.Sort.Ascending(doc => doc.StartedAtUtc))
            .ToListAsync(ct);

        return runs;
    }

    public async Task<IReadOnlyList<ProcessRunRollup>> GetRollupsAsync(
        string? processName,
        DateTime? windowStart,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        await EnsureIndexesOnceAsync(ct);

        // One grouped pass per process. The latest run's fields ride $first under
        // the newest-first sort; last-success and the in-window counts are
        // conditional accumulators. windowStart == null means unbounded counts
        // (true all-time totals), mirroring the read service's contract.
        BsonValue inWindow = windowStart is null
            ? BsonBoolean.True
            : new BsonDocument("$gte", new BsonArray { "$startedAtUtc", new BsonDateTime(windowStart.Value) });

        var pipeline = new List<BsonDocument>();

        if (processName is not null)
        {
            pipeline.Add(new BsonDocument("$match", new BsonDocument("processName", processName)));
        }

        pipeline.AddRange(
        [
            new BsonDocument("$sort", new BsonDocument { { "startedAtUtc", -1 }, { "_id", -1 } }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$processName" },
                { "latestStatus", new BsonDocument("$first", "$status") },
                // $first of a missing field yields BsonNull for docs without a
                // heartbeat, which maps back to null below.
                { "latestHeartbeatAtUtc", new BsonDocument("$first", new BsonDocument("$ifNull", new BsonArray { "$lastHeartbeatAtUtc", BsonNull.Value })) },
                { "lastRunAtUtc", new BsonDocument("$first", "$startedAtUtc") },
                // $max ignores nulls when at least one real value exists, so a
                // process with no success at all rolls up to null.
                { "lastSuccessAtUtc", new BsonDocument("$max", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$status", nameof(ProcessRunStatus.Success) }),
                        "$finishedAtUtc",
                        BsonNull.Value
                    })) },
                { "runCountInWindow", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { inWindow, 1, 0 })) },
                { "failureCountInWindow", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            inWindow,
                            // Effective status (#2474): Failed, Abandoned, and a Running row
                            // whose host stopped beating all count against the process.
                            ProcessRunStaleness.EffectiveStatusInExpression(
                                ProcessRunStatusExtensions.FailureStatuses, nowUtc)
                        }),
                        1,
                        0
                    })) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        ]);

        var rows = await context.ProcessRuns
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);

        return rows
            .Select(row => new ProcessRunRollup(
                row["_id"].AsString,
                Enum.Parse<ProcessRunStatus>(row["latestStatus"].AsString),
                AsNullableDateTime(row, "latestHeartbeatAtUtc"),
                row["lastRunAtUtc"].ToUniversalTime(),
                AsNullableDateTime(row, "lastSuccessAtUtc"),
                row["runCountInWindow"].ToInt64(),
                row["failureCountInWindow"].ToInt64()))
            .ToList();
    }

    public async Task<ProcessIterationHeaderPage> QueryIterationsAsync(
        int page, int pageSize, bool finishedOnly, IReadOnlyCollection<string>? jobModes, DateTime freshHeartbeatCutoff, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return new ProcessIterationHeaderPage([], 0);
        }

        await EnsureIndexesOnceAsync(ct);

        // Headers written before this host's first read — the history from before #2124 —
        // are built once per process; the check is two index reads, the build one $group.
        if (Volatile.Read(ref _headersBackfilled) == 0)
        {
            await _headers.BackfillIfNeededAsync(ct);
            Volatile.Write(ref _headersBackfilled, 1);
        }

        return await _headers.QueryAsync(page, pageSize, finishedOnly, jobModes, freshHeartbeatCutoff, ct);
    }

    public async Task<IReadOnlyList<ProcessRunDocument>> GetRunsForIterationsAsync(
        IReadOnlyCollection<Guid> iterationIds,
        CancellationToken ct)
    {
        if (!context.IsActive || iterationIds.Count == 0)
        {
            return [];
        }

        return await context.ProcessRuns
            .Find(Builders<ProcessRunDocument>.Filter.In(doc => doc.IterationId, iterationIds.Cast<Guid?>()))
            .Sort(Builders<ProcessRunDocument>.Sort
                .Ascending(doc => doc.StartedAtUtc)
                .Ascending(doc => doc.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProcessRunDocument>> GetLatestPerProcessAsync(
        IReadOnlyCollection<string> processNames,
        bool onlySuccesses,
        CancellationToken ct)
    {
        if (!context.IsActive || processNames.Count == 0)
        {
            return [];
        }

        await EnsureIndexesOnceAsync(ct);

        // One newest-first lookup per process, each served by ix_process_finished
        // (processName, finishedAtUtc desc): it reads the first index entry of that
        // process (or the first few, skipping non-successes). The former $in + $sort +
        // $group pipeline sorted every run of every named process in memory to keep one
        // row each, a cost that grows with the retained history (#2303).
        // The sort keys match the index exactly, so no in-memory SORT stage is planned;
        // two runs of one process finishing on the same millisecond is not a case.
        var builder = Builders<ProcessRunDocument>.Filter;
        var sort = Builders<ProcessRunDocument>.Sort
            .Ascending(doc => doc.ProcessName)
            .Descending(doc => doc.FinishedAtUtc);

        var lookups = processNames
            .Distinct(StringComparer.Ordinal)
            .Select(processName =>
            {
                var filter = builder.Eq(doc => doc.ProcessName, processName);
                if (onlySuccesses)
                {
                    filter &= builder.Eq(doc => doc.Status, ProcessRunStatus.Success);
                }

                return context.ProcessRuns.Find(filter).Sort(sort).Limit(1).FirstOrDefaultAsync(ct);
            })
            .ToList();

        var latest = await Task.WhenAll(lookups);
        return latest.OfType<ProcessRunDocument>().ToList();
    }

    public async Task<IReadOnlyList<ProcessRunBaselineSample>> GetRecentSuccessesAsync(
        string processName, string? jobMode, int limit, CancellationToken ct)
    {
        if (!context.IsActive || limit <= 0)
        {
            return [];
        }

        await EnsureIndexesOnceAsync(ct);

        // Walks ix_process_finished newest-first within one process and stops at the
        // limit; status and mode are residual filters on the fetched entries. A null
        // mode matches the runs recorded before #1362 stamped one.
        var builder = Builders<ProcessRunDocument>.Filter;
        var filter = builder.Eq(doc => doc.ProcessName, processName)
                     & builder.Eq(doc => doc.Status, ProcessRunStatus.Success)
                     & builder.Eq(doc => doc.JobMode, jobMode);

        return await context.ProcessRuns
            .Find(filter)
            .Sort(Builders<ProcessRunDocument>.Sort
                .Ascending(doc => doc.ProcessName)
                .Descending(doc => doc.FinishedAtUtc))
            .Limit(limit)
            .Project(doc => new ProcessRunBaselineSample(doc.DurationMs, doc.SummaryJson))
            .ToListAsync(ct);
    }

    public async Task<long> CountFailuresSinceAsync(
        string processName,
        DateTime? afterUtc,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return 0;
        }

        await EnsureIndexesOnceAsync(ct);

        // Judged on the effective status (#2474): a dead host's stale Running row is a failure
        // here exactly as it reads Abandoned on the panels — before, it counted for nothing and
        // the per-process alert never opened. Skipped (#1149) and Cancelled (#1513) are terminal
        // but not failures: a process that skips hourly between real runs, or one stopped by a
        // redeploy, would otherwise report a streak for doing nothing wrong.
        var builder = Builders<ProcessRunDocument>.Filter;
        var filter = builder.Eq(doc => doc.ProcessName, processName)
                     & ProcessRunStaleness.EffectiveStatusIn(ProcessRunStatusExtensions.FailureStatuses, nowUtc);

        if (afterUtc is not null)
        {
            // Strictly after: the successful run that bounds the streak must not count
            // itself as a member of it.
            filter &= builder.Gt(doc => doc.StartedAtUtc, afterUtc.Value);
        }

        return await context.ProcessRuns.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    private static FilterDefinition<ProcessRunDocument> BuildRunsFilter(
        string? processName,
        ProcessRunStatus? status,
        DateTime? since)
    {
        var builder = Builders<ProcessRunDocument>.Filter;
        var filter = builder.Empty;

        if (since is not null)
        {
            filter &= builder.Gte(doc => doc.StartedAtUtc, since.Value);
        }

        if (processName is not null)
        {
            filter &= builder.Eq(doc => doc.ProcessName, processName);
        }

        if (status is not null)
        {
            filter &= builder.Eq(doc => doc.Status, status.Value);
        }

        return filter;
    }

    private static DateTime? AsNullableDateTime(BsonDocument row, string field)
        => row.TryGetValue(field, out var value) && value.IsValidDateTime
            ? value.ToUniversalTime()
            : null;

    private async Task EnsureIndexesOnceAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) == 1)
        {
            return;
        }

        await context.EnsureProcessRunIndexesAsync(ct);
        await _headers.EnsureIndexesAsync(ct);
        Volatile.Write(ref _indexesEnsured, 1);
    }
}
