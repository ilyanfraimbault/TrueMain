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
        AuditEntry entry,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await auditLog.RecordAsync(entry.Action, entry.Actor, entry.TargetType, entry.TargetId, entry.Metadata, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Audit write failed for {Action} on {TargetType} {TargetId}; the change it records was committed.",
                entry.Action,
                entry.TargetType,
                entry.TargetId);
            return false;
        }
    }
}

/// <summary>One audit event: what was done, by whom, to what, with optional structured context.</summary>
public sealed record AuditEntry(
    string Action,
    string Actor,
    string TargetType,
    string TargetId,
    IReadOnlyDictionary<string, string>? Metadata = null);
