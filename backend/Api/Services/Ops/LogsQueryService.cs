using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads persisted <c>LogEntry</c> rows for the admin <c>/ops/logs</c>
/// panel. The <c>level</c> filter is a <b>minimum</b> threshold: passing
/// "Warning" returns Warning, Error and Critical. The column stores the
/// <c>LogLevel</c> name, so the threshold is expanded to the set of qualifying
/// names and matched with an <c>IN</c>; an unrecognised level name is ignored
/// (no level filter applied).
/// </summary>
public sealed class LogsQueryService(TrueMainDbContext db) : ILogsQueryService
{
    private const int DefaultPageSize = 50;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 200;

    public async Task<LogsReadModel> GetAsync(
        string? level,
        string? category,
        DateTime? since,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        var query = db.LogEntries.AsNoTracking();

        var levelNames = ResolveLevelThresholdNames(level);
        if (levelNames is not null)
        {
            query = query.Where(entry => levelNames.Contains(entry.Level));
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (normalizedCategory is not null)
        {
            // Case-insensitive prefix match: categories are dotted logger names
            // (e.g. "Microsoft.EntityFrameworkCore.Database.Command"), so the UI's
            // free-text "Category…" field is far more useful as a prefix filter
            // ("Microsoft" → all Microsoft.* categories) than as exact equality.
            var categoryPattern = $"{LikeEscaping.Escape(normalizedCategory)}%";
            query = query.Where(entry => EF.Functions.ILike(entry.Category, categoryPattern, LikeEscaping.EscapeChar));
        }

        if (since is not null)
        {
            query = query.Where(entry => entry.TimestampUtc >= since.Value);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (normalizedSearch is not null)
        {
            // Case-insensitive substring match on message OR exception. The
            // escaped pattern keeps user-supplied % and _ literal so a search
            // term can't turn into a wildcard scan.
            var pattern = $"%{LikeEscaping.Escape(normalizedSearch)}%";
            query = query.Where(entry =>
                EF.Functions.ILike(entry.Message, pattern, "\\")
                || (entry.Exception != null && EF.Functions.ILike(entry.Exception, pattern, "\\")));
        }

        var total = await query.LongCountAsync(ct);

        var entries = await query
            // Newest first; Id breaks ties so paging is stable when several rows
            // share a timestamp.
            .OrderByDescending(entry => entry.TimestampUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(entry => new LogEntryReadModel
            {
                Id = entry.Id,
                TimestampUtc = entry.TimestampUtc,
                Level = entry.Level,
                Category = entry.Category,
                Message = entry.Message,
                Exception = entry.Exception,
                ProcessName = entry.ProcessName,
                Host = entry.Host
            })
            .ToListAsync(ct);

        return new LogsReadModel
        {
            Entries = entries,
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize
        };
    }

    /// <summary>
    /// Expands a requested level name into the set of <c>LogLevel</c> names at or
    /// above it (the minimum-threshold semantics). Returns null when no level
    /// filter should apply: the value was blank or did not parse to a level.
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
