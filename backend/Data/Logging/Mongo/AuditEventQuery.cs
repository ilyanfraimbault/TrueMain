using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>The filters of an <c>audit_events</c> read; every one optional.</summary>
public sealed record AuditEventFilter(
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    string? Actor = null,
    DateTime? Since = null,
    DateTime? Until = null,
    string? Search = null,
    int? Page = null,
    int? PageSize = null);

/// <summary>
/// Read query over the operator-action trail (<c>audit_events</c>) for the admin Audit page and
/// the known-player History (#2288).
/// </summary>
public interface IAuditEventQuery
{
    Task<AuditEventPage> GetAsync(AuditEventFilter filter, CancellationToken ct);
}

/// <summary>One page of audit events, newest first, with the unpaged total and the paging applied.</summary>
public sealed record AuditEventPage(IReadOnlyList<AuditEventRow> Entries, long Total, int Page, int PageSize);

/// <summary>One audit event; <see cref="Id"/> is the ObjectId's hex form.</summary>
public sealed record AuditEventRow(
    string Id,
    DateTime TimestampUtc,
    string Actor,
    string Action,
    string? TargetType,
    string? TargetId,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// The Mongo side of <see cref="IAuditEventQuery"/>. Action, target type and actor are exact
/// matches, their casing resolved against the catalogs (<see cref="AuditActions"/>,
/// <see cref="AuditTargets"/>, <see cref="AuditActors"/>) so the filter stays an indexable
/// <c>$eq</c>; target id is exact; <c>since</c>/<c>until</c> bound the timestamp (inclusive,
/// exclusive); search is a case-insensitive substring over any metadata value.
/// </summary>
public sealed class MongoAuditEventQuery(MongoLogContext context) : IAuditEventQuery
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private static readonly FilterDefinitionBuilder<AuditEventDocument> Filter = Builders<AuditEventDocument>.Filter;

    public async Task<AuditEventPage> GetAsync(AuditEventFilter filter, CancellationToken ct)
    {
        var page = Math.Max(1, filter.Page ?? 1);
        var pageSize = Math.Clamp(filter.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        if (!context.IsActive)
        {
            return new AuditEventPage([], 0, page, pageSize);
        }

        var query = Build(filter);
        var total = await context.AuditEvents.CountDocumentsAsync(query, cancellationToken: ct);
        var documents = await context.AuditEvents
            .Find(query)
            .Sort(Builders<AuditEventDocument>.Sort.Descending(doc => doc.TimestampUtc).Descending(doc => doc.Id))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var rows = documents
            .Select(doc => new AuditEventRow(
                doc.Id.ToString(),
                doc.TimestampUtc,
                doc.Actor,
                doc.Action,
                doc.TargetType,
                doc.TargetId,
                doc.Metadata ?? []))
            .ToList();
        return new AuditEventPage(rows, total, page, pageSize);
    }

    private static FilterDefinition<AuditEventDocument> Build(AuditEventFilter filter)
    {
        var filters = new List<FilterDefinition<AuditEventDocument>>();
        if (Normalize(filter.Action) is { } action)
        {
            filters.Add(Filter.Eq(doc => doc.Action, Canonical(AuditActions.Labels.Select(pair => pair.Key), action)));
        }

        if (Normalize(filter.TargetType) is { } targetType)
        {
            filters.Add(Filter.Eq(doc => doc.TargetType, Canonical(AuditTargets.All, targetType)));
        }

        if (Normalize(filter.TargetId) is { } targetId)
        {
            // Persona and seed-request ids are Guids written in their lowercase "D" form.
            filters.Add(Filter.Eq(doc => doc.TargetId, Guid.TryParse(targetId, out var guid) ? guid.ToString() : targetId));
        }

        if (Normalize(filter.Actor) is { } actor)
        {
            filters.Add(Filter.Eq(doc => doc.Actor, Canonical(AuditActors.All, actor)));
        }

        if (filter.Since is { } since)
        {
            filters.Add(Filter.Gte(doc => doc.TimestampUtc, since));
        }

        if (filter.Until is { } until)
        {
            filters.Add(Filter.Lt(doc => doc.TimestampUtc, until));
        }

        if (Normalize(filter.Search) is { } search)
        {
            filters.Add(MetadataContains(search));
        }

        return filters.Count == 0 ? Filter.Empty : Filter.And(filters);
    }

    /// <summary>
    /// Any metadata value containing <paramref name="search"/>, case-insensitively. The keys vary per
    /// action, so the document is turned into its values with <c>$objectToArray</c>; the term is
    /// escaped so it matches literally. Unindexed, which the trail's size (a few events per operator
    /// action) allows.
    /// </summary>
    private static FilterDefinition<AuditEventDocument> MetadataContains(string search) =>
        new BsonDocumentFilterDefinition<AuditEventDocument>(new BsonDocument(
            "$expr",
            new BsonDocument("$anyElementTrue", new BsonArray
            {
                new BsonDocument("$map", new BsonDocument
                {
                    { "input", new BsonDocument("$objectToArray", new BsonDocument("$ifNull", new BsonArray { "$metadata", new BsonDocument() })) },
                    { "as", "entry" },
                    {
                        "in", new BsonDocument("$regexMatch", new BsonDocument
                        {
                            { "input", "$$entry.v" },
                            { "regex", Regex.Escape(search) },
                            { "options", "i" },
                        })
                    },
                }),
            })));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Canonical(IEnumerable<string> catalog, string value) =>
        catalog.FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) ?? value;
}
