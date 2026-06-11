namespace Data.Logging.Mongo;

/// <summary>
/// Writer for the operator-action audit trail (the <c>audit_events</c>
/// collection). Intentional operator write actions — seeding a main, triggering
/// an import — record one event here so there is a lossless, queryable history of
/// who did what.
/// </summary>
/// <remarks>
/// Unlike the diagnostic <c>ILogger</c> pipeline (best-effort, batched,
/// drop-on-overflow), the audit writer is <b>lossless</b>: it inserts
/// synchronously and never routes through the lossy batched channel. A write
/// failure surfaces to the caller (it does <em>not</em> silently drop), so an
/// operator action that must be auditable can be made to fail if it cannot be
/// recorded. Callers that prefer best-effort can swallow the exception
/// themselves.
/// </remarks>
public interface IAuditLog
{
    /// <summary>
    /// Records a single operator-action audit event. Inserts synchronously into
    /// the <c>audit_events</c> collection. Throws if the write fails or if the
    /// audit store is not configured.
    /// </summary>
    /// <param name="action">Stable verb-phrase key for the action (e.g. "seed_account").</param>
    /// <param name="actor">Who performed it (e.g. an operator identity or "system").</param>
    /// <param name="targetType">The kind of entity acted on (e.g. "SeedRequest").</param>
    /// <param name="targetId">The acted-on entity's identifier.</param>
    /// <param name="metadata">Optional structured context for the action.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordAsync(
        string action,
        string actor,
        string? targetType = null,
        string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// True when an audit store is configured (Mongo enabled + connection
    /// string). When false, <see cref="RecordAsync"/> is a no-op — callers in a
    /// host with no Mongo configured (e.g. some test hosts) still run normally.
    /// </summary>
    bool IsEnabled { get; }
}
