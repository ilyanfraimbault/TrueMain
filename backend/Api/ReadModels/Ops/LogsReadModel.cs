namespace TrueMain.ReadModels.Ops;

/// <summary>
/// A page of persisted application/process logs for the admin Logs panel.
/// Entries are newest-first. <see cref="Total"/> is the count of all rows
/// matching the active filters (before paging), so the panel can render a pager.
/// </summary>
public sealed record LogsReadModel
{
    public IReadOnlyList<LogEntryReadModel> Entries { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>
    /// Every known ops-event name (the static <c>OpsEvents</c> catalog, #444), so
    /// the admin Logs panel can populate its event filter without a Mongo
    /// <c>distinct</c> per load. Independent of the active filters.
    /// </summary>
    public IReadOnlyList<string> EventTypes { get; init; } = [];
}

/// <summary>
/// A single persisted log line. <see cref="Level"/> is the
/// <c>Microsoft.Extensions.Logging.LogLevel</c> name (e.g. "Warning", "Error");
/// <see cref="Exception"/> is the formatted exception when one was logged (else
/// null); <see cref="ProcessName"/> identifies the producing host ("Api" /
/// "Ingestor") when stamped.
/// </summary>
public sealed record LogEntryReadModel
{
    /// <summary>
    /// The log document's identifier. A 24-char hex string (the Mongo ObjectId)
    /// since logs moved off Postgres (#416). The admin viewer already accepts a
    /// string or numeric id, so the response shape is unchanged.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; }

    public string Level { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Exception { get; init; }

    public string? ProcessName { get; init; }

    public string? Host { get; init; }

    /// <summary>
    /// The registered ops-event name (e.g. <c>CandidateValidated</c>) when this
    /// row is a named domain event (#444); null for plain diagnostics.
    /// </summary>
    public string? EventType { get; init; }
}
