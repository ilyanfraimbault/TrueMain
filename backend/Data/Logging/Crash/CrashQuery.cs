using System.Text.RegularExpressions;
using Data.Logging.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Logging.Crash;

/// <summary>
/// Read query over the <c>crashes</c> collection backing the admin Crashes panel,
/// mirroring <see cref="MongoLogQuery"/>:
/// <list type="bullet">
///   <item><c>since</c> is a lower bound on the timestamp; results are newest-first.</item>
///   <item><c>processName</c> is an exact match ("Api" / "Ingestor").</item>
///   <item><c>source</c> is a case-insensitive exact match on the <see cref="CrashSource"/> name.</item>
///   <item><c>search</c> is a case-insensitive substring match on message OR stack trace.</item>
/// </list>
/// </summary>
public sealed class CrashQuery(MongoLogContext context) : ICrashQuery
{
    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    private static readonly FilterDefinitionBuilder<CrashReportDocument> Filter =
        Builders<CrashReportDocument>.Filter;

    private static readonly string[] KnownSources = Enum.GetNames<CrashSource>();

    public async Task<CrashPage> GetAsync(
        DateTime? since,
        string? processName,
        string? source,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        // An inactive store (no Mongo configured) yields an empty page rather than
        // throwing, so the Crashes panel degrades gracefully.
        if (!context.IsActive)
        {
            return new CrashPage([], 0, effectivePage, effectivePageSize);
        }

        var filter = BuildFilter(since, processName, source, search);

        var total = await context.Crashes.CountDocumentsAsync(filter, cancellationToken: ct);

        var documents = await context.Crashes
            .Find(filter)
            // Newest first; _id breaks ties so paging is stable when rows share a timestamp.
            .Sort(Builders<CrashReportDocument>.Sort
                .Descending(doc => doc.TimestampUtc)
                .Descending(doc => doc.Id))
            .Skip((effectivePage - 1) * effectivePageSize)
            .Limit(effectivePageSize)
            .ToListAsync(ct);

        return new CrashPage(documents.Select(ToRow).ToList(), total, effectivePage, effectivePageSize);
    }

    private static CrashRow ToRow(CrashReportDocument doc) => new(
        doc.Id.ToString(),
        doc.ReportId,
        doc.TimestampUtc,
        doc.ProcessName,
        doc.Source,
        doc.ExceptionType,
        doc.Message,
        doc.StackTrace,
        doc.InnerExceptions is null
            ? []
            : doc.InnerExceptions
                .Select(e => new CrashExceptionInfo(e.Type, e.Message, e.StackTrace))
                .ToList(),
        doc.Host,
        doc.OsDescription,
        doc.UptimeSeconds,
        doc.RuntimeVersion,
        doc.AppVersion,
        doc.WorkingSetBytes,
        doc.TotalManagedMemoryBytes,
        doc.Gen0Collections,
        doc.Gen1Collections,
        doc.Gen2Collections,
        doc.ExitCode,
        doc.RecentLogTail is null
            ? []
            : doc.RecentLogTail
                .Select(e => new CrashLogTailEntry(e.TimestampUtc, e.Level, e.Category, e.Message, e.Exception))
                .ToList());

    private static FilterDefinition<CrashReportDocument> BuildFilter(
        DateTime? since,
        string? processName,
        string? source,
        string? search)
    {
        var filters = new List<FilterDefinition<CrashReportDocument>>();

        if (since is not null)
        {
            filters.Add(Filter.Gte(doc => doc.TimestampUtc, since.Value));
        }

        var normalizedProcess = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        if (normalizedProcess is not null)
        {
            filters.Add(Filter.Eq(doc => doc.ProcessName, normalizedProcess));
        }

        var normalizedSource = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        if (normalizedSource is not null)
        {
            // Resolve casing against the static CrashSource catalog so the filter is
            // an indexable $eq (like MongoLogQuery does for eventType). An unknown
            // value falls through as-is and matches nothing.
            var canonical = KnownSources.FirstOrDefault(name =>
                    string.Equals(name, normalizedSource, StringComparison.OrdinalIgnoreCase))
                ?? normalizedSource;
            filters.Add(Filter.Eq(doc => doc.Source, canonical));
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (normalizedSearch is not null)
        {
            // Case-insensitive substring on message OR stack trace. The escaped term
            // keeps user metacharacters literal. Unindexed, so it forces a scan —
            // acceptable on the low-volume crashes collection.
            var pattern = new BsonRegularExpression(Regex.Escape(normalizedSearch), "i");
            filters.Add(Filter.Or(
                Filter.Regex(doc => doc.Message, pattern),
                Filter.Regex(doc => doc.StackTrace, pattern)));
        }

        return filters.Count == 0 ? Filter.Empty : Filter.And(filters);
    }
}
