using Data.Entities;
using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// The <c>process_iterations</c> headers behind <see cref="ProcessRunStore.QueryIterationsAsync"/>
/// (#2124): written next to every iteration-stamped run, paged by the admin.
/// </summary>
/// <remarks>
/// The iteration list used to group the whole <c>process_runs</c> collection on every request
/// and page the result, so its cost followed the 180-day history rather than the page: 158k
/// runs read for 8 iterations on preprod. A header per pass makes the page an index read and
/// the total an index count; the runs are then loaded for the page's iterations only.
/// </remarks>
internal sealed class ProcessIterationHeaders(MongoLogContext context)
{
    // A pass lasts hours at most, so a run older than every header by more than this is
    // history the headers never covered (written before them), not the leftover runs of a
    // pass whose header the TTL reaped a moment before them.
    private static readonly TimeSpan BackfillMargin = TimeSpan.FromDays(2);

    private IMongoCollection<ProcessIterationDocument> Headers =>
        field ??= context.Database.GetCollection<ProcessIterationDocument>(context.Options.ProcessIterationsCollection);

    /// <summary>
    /// Header indexes (newest-first, and per mode for the lane filter), the TTL shared with
    /// the runs, and a partial index on <c>process_runs</c> over in-flight runs only — the one
    /// read <see cref="QueryAsync"/> still makes there.
    /// </summary>
    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        await Headers.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ProcessIterationDocument>(
                    Builders<ProcessIterationDocument>.IndexKeys.Descending(doc => doc.StartedAtUtc).Descending(doc => doc.Id),
                    new CreateIndexOptions { Name = "ix_started_desc" }),
                new CreateIndexModel<ProcessIterationDocument>(
                    Builders<ProcessIterationDocument>.IndexKeys
                        .Ascending(doc => doc.JobMode)
                        .Descending(doc => doc.StartedAtUtc)
                        .Descending(doc => doc.Id),
                    new CreateIndexOptions { Name = "ix_mode_started" })
            ],
            ct);

        await MongoIndexes.ReconcileTtlAsync(Headers, doc => doc.StartedAtUtc, context.Options.ProcessRunsRetention, ct);

        await context.ProcessRuns.Indexes.CreateOneAsync(
            new CreateIndexModel<ProcessRunDocument>(
                Builders<ProcessRunDocument>.IndexKeys.Ascending(doc => doc.LastHeartbeatAtUtc),
                new CreateIndexOptions<ProcessRunDocument>
                {
                    Name = "ix_running_heartbeat",
                    PartialFilterExpression = Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Status, ProcessRunStatus.Running)
                }),
            cancellationToken: ct);
    }

    /// <summary>
    /// Creates the run's iteration header, or pulls its start back to this run's. A no-op for
    /// a run recorded outside a pass.
    /// </summary>
    public Task RecordRunAsync(ProcessRunDocument run, CancellationToken ct)
    {
        if (run.IterationId is not { } iterationId)
        {
            return Task.CompletedTask;
        }

        return Headers.UpdateOneAsync(
            Builders<ProcessIterationDocument>.Filter.Eq(doc => doc.Id, iterationId),
            Builders<ProcessIterationDocument>.Update
                .Min(doc => doc.StartedAtUtc, run.StartedAtUtc)
                .SetOnInsert(doc => doc.JobMode, run.JobMode),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    /// <summary>
    /// Builds the headers missing for runs written before they existed, once: when the oldest
    /// iteration-stamped run predates the oldest header by more than <see cref="BackfillMargin"/>.
    /// One <c>$group</c> over the runs merged into the headers, keeping any header already there.
    /// Idempotent, so two hosts racing through it on the first deploy is harmless.
    /// </summary>
    public async Task BackfillIfNeededAsync(CancellationToken ct)
    {
        var oldestRun = await context.ProcessRuns
            .Find(Builders<ProcessRunDocument>.Filter.Ne(doc => doc.IterationId, null))
            .Sort(Builders<ProcessRunDocument>.Sort.Ascending(doc => doc.StartedAtUtc))
            .Project(doc => (DateTime?)doc.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (oldestRun is null)
        {
            return;
        }

        var oldestHeader = await Headers
            .Find(FilterDefinition<ProcessIterationDocument>.Empty)
            .Sort(Builders<ProcessIterationDocument>.Sort.Ascending(doc => doc.StartedAtUtc))
            .Project(doc => (DateTime?)doc.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (oldestHeader is not null && oldestRun >= oldestHeader - BackfillMargin)
        {
            return;
        }

        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument("iterationId", new BsonDocument("$ne", BsonNull.Value))),
            new("$group", new BsonDocument
            {
                { "_id", "$iterationId" },
                { "startedAtUtc", new BsonDocument("$min", "$startedAtUtc") },
                { "jobMode", new BsonDocument("$max", "$jobMode") }
            }),
            new("$merge", new BsonDocument
            {
                { "into", context.Options.ProcessIterationsCollection },
                { "on", "_id" },
                { "whenMatched", "keepExisting" },
                { "whenNotMatched", "insert" }
            })
        ];
        await context.ProcessRuns.Aggregate<BsonDocument>(pipeline, new AggregateOptions { AllowDiskUse = true }, ct)
            .ToListAsync(ct);
    }

    /// <summary>
    /// One page of headers, newest first, and the filtered total. With
    /// <paramref name="finishedOnly"/>, the passes holding a <c>Running</c> run whose heartbeat
    /// is at or after <paramref name="freshHeartbeatCutoff"/> are left out of both — the same
    /// staleness rule the read mapping applies, so a pass whose host died reads as finished.
    /// </summary>
    public async Task<ProcessIterationHeaderPage> QueryAsync(
        int page,
        int pageSize,
        bool finishedOnly,
        IReadOnlyCollection<string>? jobModes,
        DateTime freshHeartbeatCutoff,
        CancellationToken ct)
    {
        var builder = Builders<ProcessIterationDocument>.Filter;
        var filter = jobModes is null ? builder.Empty : builder.In(doc => doc.JobMode, jobModes);

        if (finishedOnly)
        {
            var inFlight = await context.ProcessRuns
                .Find(Builders<ProcessRunDocument>.Filter.And(
                    Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Status, ProcessRunStatus.Running),
                    Builders<ProcessRunDocument>.Filter.Gte(doc => doc.LastHeartbeatAtUtc, freshHeartbeatCutoff),
                    Builders<ProcessRunDocument>.Filter.Ne(doc => doc.IterationId, null)))
                .Project(doc => doc.IterationId)
                .ToListAsync(ct);
            var inFlightIds = inFlight.OfType<Guid>().Distinct().ToList();
            if (inFlightIds.Count > 0)
            {
                filter &= builder.Nin(doc => doc.Id, inFlightIds);
            }
        }

        var total = await Headers.CountDocumentsAsync(filter, cancellationToken: ct);
        var headers = await Headers
            .Find(filter)
            .Sort(Builders<ProcessIterationDocument>.Sort.Descending(doc => doc.StartedAtUtc).Descending(doc => doc.Id))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project(doc => new ProcessIterationHeader(doc.Id, doc.StartedAtUtc))
            .ToListAsync(ct);

        return new ProcessIterationHeaderPage(headers, total);
    }
}
