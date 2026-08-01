using System.Text.RegularExpressions;
using Data.Entities;
using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for the seed-request queue. The claim (Pending→Resolving) is a
/// single guarded update — the same no-TOCTOU semantics the Postgres
/// <c>ExecuteUpdate</c> gave — and the Riot-ID idempotency lookup uses a
/// strength-2 collation (case-insensitive, accent-sensitive) instead of the
/// escaped-ILIKE dance the SQL implementation needed.
/// </summary>
internal sealed class SeedRequestStore(MongoLogContext context) : ISeedRequestStore
{
    /// <summary>
    /// Case-insensitive but accent-sensitive equality, matching Postgres ILIKE's
    /// simple case folding closely enough for Riot IDs.
    /// </summary>
    private static readonly Collation CaseInsensitive = new("en", strength: CollationStrength.Secondary);

    private int _indexesEnsured;

    public async Task InsertAsync(SeedRequestDocument request, CancellationToken ct)
    {
        ThrowIfInactive();
        await EnsureIndexesOnceAsync(ct);
        await context.SeedRequests.InsertOneAsync(request, cancellationToken: ct);
    }

    public async Task<SeedRequestDocument?> FindUnprocessedByRiotIdAsync(
        string gameName,
        string tagLine,
        string platformId,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        await EnsureIndexesOnceAsync(ct);

        var builder = Builders<SeedRequestDocument>.Filter;
        var filter = builder.In(doc => doc.Status, [SeedRequestStatus.Pending, SeedRequestStatus.Resolving])
            & builder.Eq(doc => doc.PlatformId, platformId)
            & builder.Eq(doc => doc.GameName, gameName)
            & builder.Eq(doc => doc.TagLine, tagLine);

        return await context.SeedRequests
            .Find(filter, new FindOptions { Collation = CaseInsensitive })
            .SortBy(doc => doc.RequestedAtUtc)
            .Limit(1)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SeedRequestDocument?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        return await context.SeedRequests
            .Find(doc => doc.Id == id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SeedRequestDocument>> GetRecentAsync(
        SeedRequestStatus? status,
        string? search,
        int limit,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        await EnsureIndexesOnceAsync(ct);

        var builder = Builders<SeedRequestDocument>.Filter;
        var filter = builder.Empty;

        if (status is not null)
        {
            filter &= builder.Eq(doc => doc.Status, status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Contains-search on name or tag, case-insensitive. Regex-escaped so a
            // user typing '.' or '*' searches literally (the ILIKE-escaping
            // equivalent).
            var regex = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
            filter &= builder.Regex(doc => doc.GameName, regex) | builder.Regex(doc => doc.TagLine, regex);
        }

        return await context.SeedRequests
            .Find(filter)
            // Newest-first; id breaks ties so the list is stable when several
            // documents share a RequestedAtUtc.
            .Sort(Builders<SeedRequestDocument>.Sort
                .Descending(doc => doc.RequestedAtUtc)
                .Descending(doc => doc.Id))
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SeedRequestDocument>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        await EnsureIndexesOnceAsync(ct);

        return await context.SeedRequests
            .Find(doc => doc.Status == SeedRequestStatus.Pending)
            // Oldest-first so the backlog drains fairly (FIFO).
            .Sort(Builders<SeedRequestDocument>.Sort
                .Ascending(doc => doc.RequestedAtUtc)
                .Ascending(doc => doc.Id))
            .Limit(batchSize)
            .ToListAsync(ct);
    }

    public async Task<bool> ClaimAsync(Guid id, CancellationToken ct)
    {
        ThrowIfInactive();

        // Atomic claim: flip Pending → Resolving in one guarded update so two
        // concurrent runs can't both pick the same request.
        var result = await context.SeedRequests.UpdateOneAsync(
            Builders<SeedRequestDocument>.Filter.Eq(doc => doc.Id, id)
            & Builders<SeedRequestDocument>.Filter.Eq(doc => doc.Status, SeedRequestStatus.Pending),
            Builders<SeedRequestDocument>.Update.Set(doc => doc.Status, SeedRequestStatus.Resolving),
            cancellationToken: ct);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> ResetResolvingToPendingAsync(Guid id, CancellationToken ct)
    {
        ThrowIfInactive();

        var result = await context.SeedRequests.UpdateOneAsync(
            Builders<SeedRequestDocument>.Filter.Eq(doc => doc.Id, id)
            & Builders<SeedRequestDocument>.Filter.Eq(doc => doc.Status, SeedRequestStatus.Resolving),
            Builders<SeedRequestDocument>.Update.Set(doc => doc.Status, SeedRequestStatus.Pending),
            cancellationToken: ct);

        return result.ModifiedCount > 0;
    }

    public async Task MarkIngestedAsync(
        Guid id,
        string resolvedPuuid,
        Guid? resolvedRiotAccountId,
        DateTime processedAtUtc,
        CancellationToken ct)
    {
        ThrowIfInactive();

        await context.SeedRequests.UpdateOneAsync(
            Builders<SeedRequestDocument>.Filter.Eq(doc => doc.Id, id),
            Builders<SeedRequestDocument>.Update
                .Set(doc => doc.Status, SeedRequestStatus.Ingested)
                .Set(doc => doc.Error, null)
                .Set(doc => doc.ResolvedPuuid, resolvedPuuid)
                .Set(doc => doc.ResolvedRiotAccountId, resolvedRiotAccountId)
                .Set(doc => doc.ProcessedAtUtc, processedAtUtc),
            cancellationToken: ct);
    }

    public async Task MarkFailedAsync(Guid id, string? error, DateTime processedAtUtc, CancellationToken ct)
    {
        ThrowIfInactive();

        await context.SeedRequests.UpdateOneAsync(
            Builders<SeedRequestDocument>.Filter.Eq(doc => doc.Id, id),
            Builders<SeedRequestDocument>.Update
                .Set(doc => doc.Status, SeedRequestStatus.Failed)
                .Set(doc => doc.Error, error)
                .Set(doc => doc.ProcessedAtUtc, processedAtUtc),
            cancellationToken: ct);
    }

    public async Task<SeedRequestDocument?> GetLatestResolvedForAccountAsync(
        string puuid,
        string platformId,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        return await context.SeedRequests
            .Find(doc => doc.ResolvedPuuid == puuid && doc.PlatformId == platformId)
            // Newest-first in case the same Riot ID was seeded more than once.
            .Sort(Builders<SeedRequestDocument>.Sort
                .Descending(doc => doc.RequestedAtUtc)
                .Descending(doc => doc.Id))
            .Limit(1)
            .FirstOrDefaultAsync(ct);
    }

    private void ThrowIfInactive()
    {
        if (!context.IsActive)
        {
            // Seed requests are functional data, not telemetry: silently dropping
            // an operator's request would be worse than failing loudly.
            throw new InvalidOperationException(
                "Seed requests require MongoDB (MongoLogging is disabled or has no ConnectionString).");
        }
    }

    private async Task EnsureIndexesOnceAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) == 1)
        {
            return;
        }

        await context.EnsureSeedRequestIndexesAsync(ct);
        Volatile.Write(ref _indexesEnsured, 1);
    }
}
