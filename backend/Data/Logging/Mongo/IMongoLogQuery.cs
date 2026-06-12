namespace Data.Logging.Mongo;

/// <summary>
/// Read query over the diagnostic <c>logs</c> collection for the admin
/// <c>/ops/logs</c> panel. Lives in the Data layer so the Api stays
/// persistence-ignorant and depends only on the <see cref="MongoLogPage"/>
/// read-model.
/// </summary>
public interface IMongoLogQuery
{
    Task<MongoLogPage> GetAsync(
        string? level,
        string? category,
        DateTime? since,
        string? search,
        string? eventType,
        int? page,
        int? pageSize,
        CancellationToken ct);
}

/// <summary>
/// A page of diagnostic log rows plus the unpaged total and the effective paging
/// the query actually applied (after clamping).
/// </summary>
public sealed record MongoLogPage(
    IReadOnlyList<MongoLogRow> Entries,
    long Total,
    int Page,
    int PageSize);

/// <summary>
/// A single diagnostic log row as read from Mongo. <see cref="Id"/> is the
/// 24-char hex string form of the document's ObjectId. <see cref="EventType"/> is
/// the registered ops-event name when the row is a named domain event (#444).
/// </summary>
public sealed record MongoLogRow(
    string Id,
    DateTime TimestampUtc,
    string Level,
    string Category,
    string Message,
    string? Exception,
    string? ProcessName,
    string? Host,
    string? EventType);
