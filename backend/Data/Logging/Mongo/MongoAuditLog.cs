namespace Data.Logging.Mongo;

/// <summary>
/// Mongo-backed <see cref="IAuditLog"/>. Writes operator-action audit events
/// synchronously and losslessly to the <c>audit_events</c> collection — never
/// through the lossy batched diagnostic-log channel — so an intentional operator
/// action leaves a durable trail.
/// </summary>
internal sealed class MongoAuditLog(MongoLogContext context) : IAuditLog
{
    public bool IsEnabled => context.IsActive;

    public async Task RecordAsync(
        string action,
        string actor,
        string? targetType = null,
        string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (!context.IsActive)
        {
            // No audit store configured: a no-op rather than a hard failure, so a
            // host without Mongo (e.g. a test host) still runs the operator path.
            return;
        }

        var document = new AuditEventDocument
        {
            Action = action,
            Actor = actor,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadata is { Count: > 0 }
                ? new Dictionary<string, string>(metadata)
                : null,
            TimestampUtc = DateTime.UtcNow
        };

        // Synchronous, single-document insert. Any failure propagates to the
        // caller — the audit write is lossless by design and deliberately does not
        // swallow errors the way the diagnostic sink does.
        await context.AuditEvents.InsertOneAsync(document, options: null, ct);
    }
}
