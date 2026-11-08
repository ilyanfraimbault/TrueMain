using Data.Entities;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// The lifecycle writes of a run (#2474): the Running insert, the heartbeat, the finalize and
/// the boot reconcile. Every write after the insert is filtered on
/// <see cref="ProcessRunStatus.Running"/>, the only status a run moves from
/// (<see cref="ProcessRunStatusExtensions.CanTransitionTo"/>).
/// </summary>
internal sealed partial class ProcessRunStore
{
    public async Task InsertAsync(ProcessRunDocument run, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return;
        }

        await EnsureIndexesOnceAsync(ct);
        await context.ProcessRuns.InsertOneAsync(run, cancellationToken: ct);
        await _headers.RecordRunAsync(run, ct);
    }

    public async Task<ProcessRunFinalizeOutcome> FinalizeAsync(
        Guid id,
        DateTime finishedAtUtc,
        int durationMs,
        ProcessRunStatus status,
        string? error,
        string? summaryJson,
        CancellationToken ct)
    {
        if (!status.IsTerminal())
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A run can only be finalized to a terminal status.");
        }

        if (!context.IsActive)
        {
            // Pretend the update matched so the caller doesn't insert a fallback
            // terminal document into an inactive store (it would no-op anyway).
            return ProcessRunFinalizeOutcome.Finalized;
        }

        await EnsureIndexesOnceAsync(ct);

        var update = Builders<ProcessRunDocument>.Update
            .Set(doc => doc.FinishedAtUtc, finishedAtUtc)
            .Set(doc => doc.DurationMs, durationMs)
            .Set(doc => doc.Status, status)
            .Set(doc => doc.Error, error)
            .Set(doc => doc.SummaryJson, summaryJson);

        // Guarded on Running (#2474), like every other write to a run: the only transition a
        // run takes is Running → terminal. Without the guard a run the boot reconcile had
        // already closed as Abandoned could be flipped to Success by a late finalize.
        var result = await context.ProcessRuns.UpdateOneAsync(
            Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Id, id)
            & Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Status, ProcessRunStatus.Running),
            update,
            cancellationToken: ct);

        if (result.MatchedCount > 0)
        {
            return ProcessRunFinalizeOutcome.Finalized;
        }

        var exists = await context.ProcessRuns
            .Find(Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Id, id))
            .Limit(1)
            .CountDocumentsAsync(ct) > 0;

        return exists ? ProcessRunFinalizeOutcome.AlreadyTerminal : ProcessRunFinalizeOutcome.NotFound;
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

    public async Task<int> AbandonRunningAsync(
        DateTime finishedAtUtc,
        string error,
        IReadOnlyCollection<string> processNames,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(processNames);

        if (!context.IsActive)
        {
            return 0;
        }

        await EnsureIndexesOnceAsync(ct);

        // Per-document duration (finish − that run's own start) can't ride a
        // single UpdateMany, so fetch the (few) running docs and bulk-update them.
        // Narrowed to the caller's own processes when it names them: with the pipeline
        // split into a fetch lane and an aggregate lane (#1362), each in its own process,
        // an unrestricted sweep at one lane's boot would abandon the other lane's in-flight
        // runs and make the admin panels report a healthy pipeline as broken.
        var filter = processNames.Count == 0
            ? Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Status, ProcessRunStatus.Running)
            : Builders<ProcessRunDocument>.Filter.And(
                Builders<ProcessRunDocument>.Filter.Eq(doc => doc.Status, ProcessRunStatus.Running),
                Builders<ProcessRunDocument>.Filter.In(doc => doc.ProcessName, processNames));

        var running = await context.ProcessRuns
            .Find(filter)
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
}
