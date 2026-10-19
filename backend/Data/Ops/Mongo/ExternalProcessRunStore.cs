using Data.Entities;
using Data.Logging.Mongo;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for <see cref="IExternalProcessRunStore"/>. Every write is guarded on
/// <see cref="ProcessRunStatus.Running"/>, like the recorder's heartbeat. Inactive Mongo
/// reads as "no such run", which the API answers with a 404 the scripts ignore.
/// </summary>
internal sealed class ExternalProcessRunStore(MongoLogContext context) : IExternalProcessRunStore
{
    private static readonly FilterDefinitionBuilder<ProcessRunDocument> Filter = Builders<ProcessRunDocument>.Filter;

    public async Task<ProcessRunDocument?> GetAsync(Guid id, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        return await context.ProcessRuns.Find(Filter.Eq(doc => doc.Id, id)).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> HeartbeatAsync(Guid id, DateTime nowUtc, string? summaryJson, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return false;
        }

        var update = Builders<ProcessRunDocument>.Update.Set(doc => doc.LastHeartbeatAtUtc, nowUtc);
        if (summaryJson is not null)
        {
            update = update.Set(doc => doc.SummaryJson, summaryJson);
        }

        var result = await context.ProcessRuns.UpdateOneAsync(RunningRun(id), update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> FinishAsync(
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
            return false;
        }

        var update = Builders<ProcessRunDocument>.Update
            .Set(doc => doc.FinishedAtUtc, finishedAtUtc)
            .Set(doc => doc.DurationMs, durationMs)
            .Set(doc => doc.Status, status)
            .Set(doc => doc.Error, error)
            .Set(doc => doc.LastHeartbeatAtUtc, finishedAtUtc);
        if (summaryJson is not null)
        {
            update = update.Set(doc => doc.SummaryJson, summaryJson);
        }

        var result = await context.ProcessRuns.UpdateOneAsync(RunningRun(id), update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    private static FilterDefinition<ProcessRunDocument> RunningRun(Guid id) =>
        Filter.Eq(doc => doc.Id, id) & Filter.Eq(doc => doc.Status, ProcessRunStatus.Running);
}
