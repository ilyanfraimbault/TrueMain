using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Logging.Mongo;

/// <summary>
/// Purpose-built read query over the diagnostic <c>logs</c> collection backing the
/// admin <c>/ops/logs</c> panel. Keeps Mongo concerns inside the Data layer: the
/// Api stays persistence-ignorant and consumes the <see cref="MongoLogPage"/>
/// read-model. Preserves the exact filter semantics of the old Postgres
/// <c>LogsQueryService</c>:
/// <list type="bullet">
///   <item><c>level</c> is a <b>minimum</b> threshold (Warning ⇒ Warning+Error+Critical).</item>
///   <item><c>category</c> is a case-insensitive <b>prefix</b> match.</item>
///   <item><c>search</c> is a case-insensitive <b>substring</b> match on message OR exception.</item>
///   <item><c>since</c> is a lower bound on the timestamp; results are newest-first.</item>
///   <item><c>eventType</c> is a case-insensitive <b>exact</b> match on the
///   ops-event name (#444); rows that are not named events never match.</item>
///   <item><c>process</c> is a case-insensitive <b>exact</b> match on the
///   producing process name ("Api" / "Ingestor"); rows without a stamped
///   process never match.</item>
///   <item><c>hasException</c> true restricts to rows carrying a formatted
///   exception; false/null applies no exception filter.</item>
/// </list>
/// </summary>
public sealed class MongoLogQuery(MongoLogContext context) : IMongoLogQuery
{
    private const int DefaultPageSize = 50;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 200;

    private static readonly FilterDefinitionBuilder<MongoLogDocument> Filter =
        Builders<MongoLogDocument>.Filter;

    public async Task<MongoLogPage> GetAsync(
        string? level,
        string? category,
        DateTime? since,
        string? search,
        string? eventType,
        string? process,
        bool? hasException,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        // An inactive store (no Mongo configured) yields an empty page rather than
        // throwing, so /ops/logs degrades gracefully.
        if (!context.IsActive)
        {
            return new MongoLogPage([], 0, effectivePage, effectivePageSize);
        }

        var filter = BuildFilter(level, category, since, search, eventType, process, hasException);

        var total = await context.Logs.CountDocumentsAsync(filter, cancellationToken: ct);

        var documents = await context.Logs
            .Find(filter)
            // Newest first; _id breaks ties so paging is stable when several rows
            // share a timestamp (ObjectId is monotonic within a process).
            .Sort(Builders<MongoLogDocument>.Sort
                .Descending(doc => doc.TimestampUtc)
                .Descending(doc => doc.Id))
            .Skip((effectivePage - 1) * effectivePageSize)
            .Limit(effectivePageSize)
            .ToListAsync(ct);

        var rows = documents.Select(doc => new MongoLogRow(
            doc.Id.ToString(),
            doc.TimestampUtc,
            doc.Level,
            doc.Category,
            doc.Message,
            doc.Exception,
            doc.ProcessName,
            doc.Host,
            doc.EventType)).ToList();

        return new MongoLogPage(rows, total, effectivePage, effectivePageSize);
    }

    private static FilterDefinition<MongoLogDocument> BuildFilter(
        string? level,
        string? category,
        DateTime? since,
        string? search,
        string? eventType,
        string? process,
        bool? hasException)
    {
        var filters = new List<FilterDefinition<MongoLogDocument>>();

        var levelNames = ResolveLevelThresholdNames(level);
        if (levelNames is not null)
        {
            filters.Add(Filter.In(doc => doc.Level, levelNames));
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (normalizedCategory is not null)
        {
            // Case-insensitive prefix match: categories are dotted logger names, so
            // the UI's free-text "Category…" field is most useful as a prefix
            // filter. Anchor at the start and escape the term so it matches
            // literally (no user-supplied regex metacharacters).
            var pattern = new BsonRegularExpression(
                $"^{Regex.Escape(normalizedCategory)}", "i");
            filters.Add(Filter.Regex(doc => doc.Category, pattern));
        }

        if (since is not null)
        {
            filters.Add(Filter.Gte(doc => doc.TimestampUtc, since.Value));
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (normalizedSearch is not null)
        {
            // Case-insensitive substring match on message OR exception. The escaped
            // term keeps user-supplied metacharacters literal so a search can't turn
            // into a wildcard scan.
            // TODO: message/exception are unindexed, so this regex forces a
            // collection scan. If log volume grows enough that search becomes hot,
            // add a $text (or Atlas Search) index over message/exception and switch
            // this branch to a $text query.
            var pattern = new BsonRegularExpression(Regex.Escape(normalizedSearch), "i");
            filters.Add(Filter.Or(
                Filter.Regex(doc => doc.Message, pattern),
                Filter.Regex(doc => doc.Exception, pattern)));
        }

        var normalizedEventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();
        if (normalizedEventType is not null)
        {
            // Exact match, case-insensitive: the UI sends catalog values verbatim,
            // but a hand-typed query must not fail on casing. The casing is
            // resolved against the static catalog in memory so the filter is an
            // indexable $eq (a case-insensitive regex cannot be served from the
            // sparse ix_event_type index). An unknown name falls through as-is and
            // matches nothing — same outcome as the regex; rows without an
            // eventType (plain diagnostics) never match.
            var canonical = OpsEvents.KnownEventTypes.FirstOrDefault(name =>
                    string.Equals(name, normalizedEventType, StringComparison.OrdinalIgnoreCase))
                ?? normalizedEventType;
            filters.Add(Filter.Eq(doc => doc.EventType, canonical));
        }

        var normalizedProcess = string.IsNullOrWhiteSpace(process) ? null : process.Trim();
        if (normalizedProcess is not null)
        {
            // Exact match, case-insensitive: the UI sends the catalog values
            // ("Api"/"Ingestor") verbatim, but a hand-typed query must not fail on
            // casing. Resolved against the known catalog in memory so the filter
            // stays an indexable $eq; an unknown name falls through as-is and
            // matches nothing. Rows without a stamped process never match.
            var canonical = LogProcesses.KnownProcessNames.FirstOrDefault(name =>
                    string.Equals(name, normalizedProcess, StringComparison.OrdinalIgnoreCase))
                ?? normalizedProcess;
            filters.Add(Filter.Eq(doc => doc.ProcessName, canonical));
        }

        if (hasException is true)
        {
            // Only rows carrying a formatted exception — the "show me the crashes
            // and faults" view. False is deliberately a no-op rather than an
            // exception-free filter: operators either want everything or only the
            // exceptional rows.
            filters.Add(Filter.Ne(doc => doc.Exception, null));
        }

        return filters.Count == 0 ? Filter.Empty : Filter.And(filters);
    }

    /// <summary>
    /// Expands a requested level name into the set of <c>LogLevel</c> names at or
    /// above it (minimum-threshold semantics). Returns null when no level filter
    /// should apply: the value was blank or did not parse to a level.
    /// </summary>
    private static IReadOnlyList<string>? ResolveLevelThresholdNames(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)
            || !Enum.TryParse<LogLevel>(level.Trim(), ignoreCase: true, out var minimum)
            || minimum == LogLevel.None)
        {
            return null;
        }

        return Enum.GetValues<LogLevel>()
            .Where(value => value != LogLevel.None && value >= minimum)
            .Select(value => value.ToString())
            .ToList();
    }
}
