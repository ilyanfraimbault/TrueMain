using Data.Entities;
using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for recorded process runs. Aggregation-heavy reads (rollup,
/// iteration grouping, latest-per-process) are written as explicit pipelines over
/// the document field names, mirroring the SQL the Postgres implementation used
/// (grouped scans + DISTINCT ON), with the same tie-breaks (id desc) so paging
/// and latest-run picks stay deterministic.
/// </summary>
internal sealed class ProcessRunStore(MongoLogContext context) : IProcessRunStore
{
    // Same first-use index bootstrap as DbStorageSnapshotStore: no sink to hang
    // startup index creation off, so ensure lazily; the flag is only set after a
    // success so a transient Mongo failure retries on the next call.
    private int _indexesEnsured;

    public async Task InsertAsync(ProcessRunDocument run, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return;
        }

        await EnsureIndexesOnceAsync(ct);
        await context.ProcessRuns.InsertOneAsync(run, cancellationToken: ct);
    }

    public async Task<bool> FinalizeAsync(
        Guid id,
        DateTime finishedAtUtc,
        int durationMs,
        ProcessRunStatus status,
        string? error,
        string? summaryJson,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            // Pretend the update matched so the caller doesn't insert a fallback
            // terminal document into an inactive store (it would no-op anyway).
            return true;
        }

        await EnsureIndexesOnceAsync(ct);

        var update = Builders<ProcessRunDocument>.Update
            .Set(doc => doc.FinishedAtUtc, finishedAtUtc)
            .Set(doc => doc.DurationMs, durationMs)
            .Set(doc => doc.Status, status)
            .Set(doc => doc.Error, error)
            .Set(doc => doc.SummaryJson, summaryJson);

        var result = await context.ProcessRuns.UpdateOneAsync(
            Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Id, id),
            update,
            cancellationToken: ct);

        return result.MatchedCount > 0;
    }

    public async Task TouchHeartbeatAsync(Guid id, DateTime nowUtc, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return;
        }

        // Guarded on Status == Running: a no-op when the run is gone or already
        // terminal — refreshing a finished row would resurrect it as "fresh".
        await context.ProcessRuns.UpdateOneAsync(
            Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Id, id)
            & Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Status, ProcessRunStatus.Running),
            Builders<ProcessRunDocument>.Update.Set(doc => doc.LastHeartbeatAtUtc, nowUtc),
            cancellationToken: ct);
    }

    public async Task<int> AbandonRunningAsync(DateTime finishedAtUtc, string error, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return 0;
        }

        await EnsureIndexesOnceAsync(ct);

        // Per-document duration (finish − that run's own start) can't ride a
        // single UpdateMany, so fetch the (few) running docs and bulk-update them.
        var running = await context.ProcessRuns
            .Find(doc => doc.Status == ProcessRunStatus.Running)
            .ToListAsync(ct);

        if (running.Count == 0)
        {
            return 0;
        }

        var writes = running
            .Select(doc =>
            {
                // Clamp before the int cast: an orphaned run can be arbitrarily old,
                // and a span over int.MaxValue ms would overflow to negative.
                var durationMs = (int)Math.Clamp(
                    (finishedAtUtc - doc.StartedAtUtc).TotalMilliseconds, 0, int.MaxValue);

                var update = Builders<ProcessRunDocument>.Update
                    .Set(d => d.Status, ProcessRunStatus.Abandoned)
                    .Set(d => d.FinishedAtUtc, finishedAtUtc)
                    .Set(d => d.DurationMs, durationMs)
                    .Set(d => d.Error, error);

                return (WriteModel<ProcessRunDocument>)new UpdateOneModel<ProcessRunDocument>(
                    // Re-check Running so a run that finished between the read and
                    // this write keeps its real terminal state.
                    Builders<ProcessRunDocument>.Filter.Eq(d => d.Id, doc.Id)
                    & Builders<ProcessRunDocument>.Filter.Eq(d => d.Status, ProcessRunStatus.Running),
                    update);
            })
            .ToList();

        var result = await context.ProcessRuns.BulkWriteAsync(
            writes, new BulkWriteOptions { IsOrdered = false }, ct);

        return (int)result.ModifiedCount;
    }

    public async Task<DateTime?> GetLastCompletedRunStartAsync(string processName, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        var latest = await context.ProcessRuns
            .Find(doc => doc.ProcessName == processName && doc.Status != ProcessRunStatus.Running)
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

    public async Task<IReadOnlyList<ProcessRunRollup>> GetRollupsAsync(
        string? processName,
        DateTime? windowStart,
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
                            new BsonDocument("$eq", new BsonArray { "$status", nameof(ProcessRunStatus.Failed) })
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
        int page,
        int pageSize,
        bool finishedOnly,
        DateTime freshHeartbeatCutoff,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return new ProcessIterationHeaderPage([], 0);
        }

        await EnsureIndexesOnceAsync(ct);

        var pipeline = new List<BsonDocument>
        {
            // Only iteration-stamped runs group into the chain view ($ne: null
            // also excludes documents without the field).
            new("$match", new BsonDocument("iterationId", new BsonDocument("$ne", BsonNull.Value))),
            new("$group", new BsonDocument
            {
                { "_id", "$iterationId" },
                { "startedAtUtc", new BsonDocument("$min", "$startedAtUtc") },
                // An iteration is in flight when it has a Running run with a
                // still-fresh heartbeat — the same staleness rule the read mapping
                // uses. $gte against a missing/null heartbeat is false.
                { "hasFreshRunning", new BsonDocument("$max", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$status", nameof(ProcessRunStatus.Running) }),
                            new BsonDocument("$gte", new BsonArray
                            {
                                new BsonDocument("$ifNull", new BsonArray { "$lastHeartbeatAtUtc", BsonNull.Value }),
                                new BsonDateTime(freshHeartbeatCutoff)
                            })
                        }),
                        1,
                        0
                    })) }
            })
        };

        if (finishedOnly)
        {
            pipeline.Add(new BsonDocument("$match", new BsonDocument("hasFreshRunning", 0)));
        }

        pipeline.AddRange(
        [
            new BsonDocument("$sort", new BsonDocument { { "startedAtUtc", -1 }, { "_id", -1 } }),
            new BsonDocument("$facet", new BsonDocument
            {
                { "total", new BsonArray { new BsonDocument("$count", "n") } },
                { "page", new BsonArray
                    {
                        new BsonDocument("$skip", (page - 1) * pageSize),
                        new BsonDocument("$limit", pageSize)
                    } }
            })
        ]);

        var facet = await context.ProcessRuns
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .FirstAsync(ct);

        var totalArray = facet["total"].AsBsonArray;
        var total = totalArray.Count == 0 ? 0L : totalArray[0].AsBsonDocument["n"].ToInt64();

        var headers = facet["page"].AsBsonArray
            .Select(item => item.AsBsonDocument)
            .Select(doc => new ProcessIterationHeader(
                Guid.Parse(doc["_id"].AsString),
                doc["startedAtUtc"].ToUniversalTime()))
            .ToList();

        return new ProcessIterationHeaderPage(headers, total);
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

        // The Mongo shape of Postgres' DISTINCT ON: newest-first by finish time,
        // then $first of the whole document per process.
        var match = new BsonDocument("processName", new BsonDocument("$in", new BsonArray(processNames)));
        if (onlySuccesses)
        {
            match.Add("status", nameof(ProcessRunStatus.Success));
        }

        var pipeline = new List<BsonDocument>
        {
            new("$match", match),
            new("$sort", new BsonDocument { { "finishedAtUtc", -1 }, { "_id", -1 } }),
            new("$group", new BsonDocument
            {
                { "_id", "$processName" },
                { "doc", new BsonDocument("$first", "$$ROOT") }
            }),
            new("$replaceRoot", new BsonDocument("newRoot", "$doc"))
        };

        return await context.ProcessRuns
            .Aggregate<ProcessRunDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);
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
        Volatile.Write(ref _indexesEnsured, 1);
    }
}
