using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// Audit as the callers use it: best effort, after the change it records is committed.
/// </summary>
public static class AuditLogBestEffort
{
    /// <summary>
    /// Records the event, or logs one warning when the audit store cannot take it: the change is
    /// already committed, so a Mongo outage must cost the audit event, never the request or the
    /// run that made the change (a seed flipped to Failed after it succeeded, an edit answered
    /// with a 500). The warning names the action and the target id only — nothing a request
    /// carried reaches the log line.
    /// </summary>
    /// <returns>Whether the event was recorded.</returns>
    public static async Task<bool> TryRecordAsync(
        this IAuditLog auditLog,
        ILogger logger,
        string action,
        string actor,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        try
        {
            await auditLog.RecordAsync(action, actor, targetType, targetId, metadata, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Audit write failed for {Action} on {TargetType} {TargetId}; the change it records was committed.",
                action,
                targetType,
                targetId);
            return false;
        }
    }
}
