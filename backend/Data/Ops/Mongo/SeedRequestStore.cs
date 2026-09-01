using System.Text.RegularExpressions;
using Core.Truemains;
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

    /// <summary>
    /// Newest-first; id breaks ties so paging is stable when several documents
    /// share a RequestedAtUtc — which a bulk seeder run makes routine rather than
    /// exceptional, and without which a row could appear on two pages or on none.
    /// </summary>
    private static readonly SortDefinition<SeedRequestDocument> NewestFirst =
        Builders<SeedRequestDocument>.Sort
            .Descending(doc => doc.RequestedAtUtc)
            .Descending(doc => doc.Id);

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

        return await context.SeedRequests
            .Find(BuildFilter(status, search, platformId: null))
            .Sort(NewestFirst)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<SeedRequestPage> GetPageAsync(
        SeedRequestStatus? status,
        string? search,
        string? platformId,
        int skip,
        int take,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return new SeedRequestPage([], 0);
        }

        await EnsureIndexesOnceAsync(ct);

        var filter = BuildFilter(status, search, platformId);

        // Counted before the page is read, with the same filter and no skip/take.
        // CountDocuments rather than EstimatedDocumentCount: the latter ignores the
        // filter entirely and would report the whole collection for every filtered
        // view, which is exactly the number the pager must not show.
        var total = await context.SeedRequests.CountDocumentsAsync(filter, cancellationToken: ct);

        var requests = await context.SeedRequests
            .Find(filter)
            .Sort(NewestFirst)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(ct);

        return new SeedRequestPage(requests, total);
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

    /// <summary>
    /// The filter shared by the unpaged scan and the paged read, so the two cannot
    /// drift into disagreeing about what a search matches.
    /// </summary>
    private static FilterDefinition<SeedRequestDocument> BuildFilter(
        SeedRequestStatus? status,
        string? search,
        string? platformId)
    {
        var builder = Builders<SeedRequestDocument>.Filter;
        var filter = builder.Empty;

        if (status is not null)
        {
            filter &= builder.Eq(doc => doc.Status, status.Value);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            // Stored canonical (upper-case, from PlatformId.TryParse on the write
            // path), so an exact match is right and avoids a collation scan.
            filter &= builder.Eq(doc => doc.PlatformId, platformId.Trim().ToUpperInvariant());
        }

        var (namePart, tagPart) = RiotIdSearchTerm.Split(search);

        if (tagPart is not null)
        {
            // The term carries a '#', so it is a Riot ID and the two halves are
            // matched against the two fields they name. Anything else would find
            // nothing at all: no document stores the joined "Name#TAG" string.
            filter &= builder.Regex(doc => doc.TagLine, Contains(tagPart));

            if (namePart is not null)
            {
                filter &= builder.Regex(doc => doc.GameName, Contains(namePart));
            }
        }
        else if (namePart is not null)
        {
            // No '#': the term is one fragment, matched against either field so a
            // bare tag ("KR1") still finds its rows.
            var regex = Contains(namePart);
            filter &= builder.Regex(doc => doc.GameName, regex) | builder.Regex(doc => doc.TagLine, regex);
        }

        return filter;
    }

    /// <summary>
    /// Case-insensitive contains-match. Regex-escaped so a user typing '.' or
    /// '*' searches literally (the ILIKE-escaping equivalent).
    /// </summary>
    private static BsonRegularExpression Contains(string fragment)
        => new(Regex.Escape(fragment), "i");

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
