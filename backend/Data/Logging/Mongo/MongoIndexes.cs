using System.Linq.Expressions;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>
/// The index chores every observability collection shares: retiring a superseded index and
/// keeping a TTL index in step with its configured retention. Out of
/// <see cref="MongoLogContext"/> so a store that owns its collection (the desktop telemetry,
/// #1805) reconciles its retention the same way the context's collections do.
/// </summary>
internal static class MongoIndexes
{
    private const string TtlIndexName = "ttl_timestamp";

    /// <summary>
    /// Drops <paramref name="indexName"/> when it exists, so a superseded index can be
    /// retired without the caller having to know whether this deployment has already
    /// run. Listing first keeps the steady-state no-op free of an exception
    /// round-trip; the drop still tolerates the index having vanished in between,
    /// because check-then-act races two hosts ensuring indexes at the same time (an
    /// overlapping redeploy is exactly when both would run this).
    /// </summary>
    public static async Task DropIfExistsAsync<TDoc>(
        IMongoCollection<TDoc> collection,
        string indexName,
        CancellationToken ct)
    {
        using var cursor = await collection.Indexes.ListAsync(ct);
        var indexes = await cursor.ToListAsync(ct);

        var exists = indexes.Any(
            index => index.TryGetValue("name", out var name)
                     && name.IsString
                     && name.AsString == indexName);

        if (!exists)
        {
            return;
        }

        try
        {
            await collection.Indexes.DropOneAsync(indexName, ct);
        }
        catch (MongoCommandException ex) when (ex.CodeName == "IndexNotFound")
        {
            // Another host dropped it between the list and the drop. The desired end
            // state is "gone", and it is gone.
        }
    }

    /// <summary>
    /// Reconciles the native TTL index on <paramref name="collection"/>'s
    /// timestamp field with the configured <paramref name="retention"/> window
    /// (e.g. <see cref="MongoLoggingOptions.LogsRetention"/> for <c>logs</c>,
    /// <see cref="MongoLoggingOptions.RiotApiCallsRetention"/> for
    /// <c>riot_api_call_rollups</c>):
    /// <list type="bullet">
    /// <item>retention &lt;= 0 → drop the TTL index if present (retain indefinitely);</item>
    /// <item>no TTL index yet → create it;</item>
    /// <item>TTL index exists with a different <c>expireAfterSeconds</c> → drop and
    /// recreate so the new window takes effect (re-creating with the same name and
    /// different options would otherwise throw <c>IndexOptionsConflict</c>);</item>
    /// <item>TTL index already matches → no-op.</item>
    /// </list>
    /// Mongo's background reaper then deletes documents whose <c>timestampUtc</c> is
    /// older than the window. Ascending key is required for a TTL index.
    /// </summary>
    public static async Task ReconcileTtlAsync<TDoc>(
        IMongoCollection<TDoc> collection,
        Expression<Func<TDoc, object?>> timestampField,
        TimeSpan retention,
        CancellationToken ct)
    {
        var existing = await GetTtlExpireAfterSecondsAsync(collection, ct);

        if (retention <= TimeSpan.Zero)
        {
            // Retention disabled: tear down any TTL index left from a prior config
            // so documents are kept indefinitely.
            if (existing is not null)
            {
                await collection.Indexes.DropOneAsync(TtlIndexName, ct);
            }

            return;
        }

        var desiredSeconds = (long)retention.TotalSeconds;

        // Already present with the same window: nothing to do.
        if (existing == desiredSeconds)
        {
            return;
        }

        // Present but with a stale window: drop it first, since re-creating a
        // same-name index with different options would throw IndexOptionsConflict.
        if (existing is not null)
        {
            await collection.Indexes.DropOneAsync(TtlIndexName, ct);
        }

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<TDoc>(
                Builders<TDoc>.IndexKeys.Ascending(timestampField),
                new CreateIndexOptions
                {
                    Name = TtlIndexName,
                    ExpireAfter = retention
                }),
            cancellationToken: ct);
    }

    /// <summary>
    /// Returns the <c>expireAfterSeconds</c> of the existing TTL index on
    /// <paramref name="collection"/>, or <c>null</c> when no such index exists.
    /// Reads the raw index document so it works regardless of how the value was
    /// originally written.
    /// </summary>
    private static async Task<long?> GetTtlExpireAfterSecondsAsync<TDoc>(
        IMongoCollection<TDoc> collection,
        CancellationToken ct)
    {
        using var cursor = await collection.Indexes.ListAsync(ct);
        var indexes = await cursor.ToListAsync(ct);

        var ttl = indexes.FirstOrDefault(
            index => index.TryGetValue("name", out var name)
                     && name.IsString
                     && name.AsString == TtlIndexName);

        if (ttl is null || !ttl.TryGetValue("expireAfterSeconds", out var expire))
        {
            return null;
        }

        // expireAfterSeconds is typically stored as an Int32/Int64; ToInt64 handles
        // either numeric representation.
        return expire.ToInt64();
    }
}
