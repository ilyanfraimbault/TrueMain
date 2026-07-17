using Data.Logging;
using Data.Logging.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads persisted diagnostic logs for the admin <c>/ops/logs</c> panel. Thin
/// adapter over the Data-layer <see cref="IMongoLogQuery"/>: the Mongo filter
/// semantics and paging live in Data (so the Api stays persistence-ignorant);
/// this service maps the <see cref="MongoLogPage"/> read-model onto the
/// <see cref="LogsReadModel"/> API contract. The contract extends the previous
/// Postgres implementation (#416), so the admin viewer keeps working: the
/// <c>level</c> filter is a <b>minimum</b> threshold, <c>category</c> is a prefix
/// match, <c>search</c> matches message/exception case-insensitively, and
/// <c>eventType</c> (#444) is a case-insensitive exact match on the ops-event
/// name, <c>process</c> a case-insensitive exact match on the producing host
/// ("Api"/"Ingestor"), and <c>hasException</c> true restricts to rows carrying a
/// formatted exception. The known event and process names ride on every response
/// (static <see cref="OpsEvents"/>/<see cref="LogProcesses"/> catalogs — no
/// Mongo <c>distinct</c>).
/// </summary>
public sealed class LogsQueryService(IMongoLogQuery query) : ILogsQueryService
{
    public async Task<LogsReadModel> GetAsync(
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
        var result = await query.GetAsync(
            level, category, since, search, eventType, process, hasException, page, pageSize, ct);

        return new LogsReadModel
        {
            Entries = result.Entries
                .Select(row => new LogEntryReadModel
                {
                    Id = row.Id,
                    TimestampUtc = row.TimestampUtc,
                    Level = row.Level,
                    Category = row.Category,
                    Message = row.Message,
                    Exception = row.Exception,
                    ProcessName = row.ProcessName,
                    Host = row.Host,
                    EventType = row.EventType
                })
                .ToList(),
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize,
            EventTypes = OpsEvents.KnownEventTypes,
            Processes = LogProcesses.KnownProcessNames
        };
    }
}
