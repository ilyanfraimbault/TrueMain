using Data.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// The effective-status rule for a recorded run: a <see cref="ProcessRunStatus.Running"/> row
/// whose heartbeat went stale reads as <see cref="ProcessRunStatus.Abandoned"/>. Owned by Data
/// (#2474) so the store's own counts — the failure streak, the cadence gate, the rollup — judge
/// a dead host's run the way the admin panels show it, instead of only the read mapping doing so.
/// </summary>
public static class ProcessRunStaleness
{
    /// <summary>
    /// How often the ingestor refreshes a running run's heartbeat — and, on the same cadence,
    /// its container liveness file, since a signal that only ticks at pass boundaries cannot
    /// tell a wedged process from a slow one (#1229).
    /// </summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A Running row whose heartbeat is older than this — or missing entirely — is
    /// treated as Abandoned: its owner died without finalising it. Four
    /// <see cref="HeartbeatInterval"/>s, so a healthy-but-slow run that misses a beat
    /// or two doesn't flap to Abandoned.
    /// </summary>
    public static readonly TimeSpan Threshold = HeartbeatInterval * 4;

    /// <summary>
    /// Maps a run's stored status to its effective status: a Running row reads as
    /// Abandoned once its heartbeat is missing or older than <see cref="Threshold"/>;
    /// every other status passes through unchanged.
    /// </summary>
    public static ProcessRunStatus EffectiveStatus(ProcessRunStatus status, DateTime? lastHeartbeatAtUtc, DateTime now)
    {
        if (status != ProcessRunStatus.Running)
        {
            return status;
        }

        var stale = lastHeartbeatAtUtc is null || lastHeartbeatAtUtc < now - Threshold;
        return stale ? ProcessRunStatus.Abandoned : ProcessRunStatus.Running;
    }

    /// <summary>
    /// A find filter matching the runs whose <em>effective</em> status (as of
    /// <paramref name="now"/>) is one of <paramref name="statuses"/>.
    /// </summary>
    internal static FilterDefinition<ProcessRunDocument> EffectiveStatusIn(
        IReadOnlyCollection<ProcessRunStatus> statuses,
        DateTime now)
    {
        var builder = Builders<ProcessRunDocument>.Filter;
        var cutoff = now - Threshold;

        var stored = statuses.Where(status => status != ProcessRunStatus.Running).ToList();
        var filters = new List<FilterDefinition<ProcessRunDocument>> { builder.In(doc => doc.Status, stored) };

        var running = builder.Eq(doc => doc.Status, ProcessRunStatus.Running);
        if (statuses.Contains(ProcessRunStatus.Abandoned))
        {
            // Eq(null) matches a missing field as well as an explicit null.
            filters.Add(running & (builder.Eq(doc => doc.LastHeartbeatAtUtc, null)
                                   | builder.Lt(doc => doc.LastHeartbeatAtUtc, cutoff)));
        }

        if (statuses.Contains(ProcessRunStatus.Running))
        {
            filters.Add(running & builder.Gte(doc => doc.LastHeartbeatAtUtc, cutoff));
        }

        return builder.Or(filters);
    }

    /// <summary>
    /// The aggregation-expression form of <see cref="EffectiveStatusIn"/>, for <c>$cond</c>
    /// accumulators over the raw document fields.
    /// </summary>
    internal static BsonDocument EffectiveStatusInExpression(
        IReadOnlyCollection<ProcessRunStatus> statuses,
        DateTime now)
    {
        var cutoff = new BsonDateTime(now - Threshold);
        var stored = new BsonArray(statuses
            .Where(status => status != ProcessRunStatus.Running)
            .Select(status => status.ToString()));

        var branches = new BsonArray { new BsonDocument("$in", new BsonArray { "$status", stored }) };
        var isRunning = new BsonDocument("$eq", new BsonArray { "$status", nameof(ProcessRunStatus.Running) });
        // A missing or null heartbeat compares below every date in BSON order, so $lt covers it.
        var heartbeat = new BsonDocument("$ifNull", new BsonArray { "$lastHeartbeatAtUtc", BsonNull.Value });

        if (statuses.Contains(ProcessRunStatus.Abandoned))
        {
            branches.Add(new BsonDocument("$and", new BsonArray
            {
                isRunning,
                new BsonDocument("$lt", new BsonArray { heartbeat, cutoff })
            }));
        }

        if (statuses.Contains(ProcessRunStatus.Running))
        {
            branches.Add(new BsonDocument("$and", new BsonArray
            {
                isRunning,
                new BsonDocument("$gte", new BsonArray { heartbeat, cutoff })
            }));
        }

        return new BsonDocument("$or", branches);
    }
}
