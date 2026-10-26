using Data.Logging.Mongo;

namespace TrueMain.Services.Personas;

/// <summary>
/// The audit trail of the operator's edits of known players (#2019), one event per action, like
/// <c>resolve_persona_conflict</c> (#2253). Best effort: the edit is already committed when it is
/// recorded, so a Mongo outage logs a warning instead of failing the request.
/// </summary>
public sealed class PersonaAudit(IAuditLog auditLog, ILogger<PersonaAudit> logger)
{
    public Task RecordAsync(
        string action, Guid personaId, string slug, Dictionary<string, string> metadata, CancellationToken ct) =>
        RecordAsync(action, personaId, slug, metadata, "operator", ct);

    /// <summary>The same, under <paramref name="actor"/>: <c>auto</c> for the same-name merges (#2271, #2286).</summary>
    public async Task RecordAsync(
        string action, Guid personaId, string slug, Dictionary<string, string> metadata, string actor, CancellationToken ct)
    {
        try
        {
            metadata["slug"] = slug;
            await auditLog.RecordAsync(
                action: action,
                actor: actor,
                targetType: "Persona",
                targetId: personaId.ToString(),
                metadata: metadata,
                ct: ct);
        }
        catch (Exception ex)
        {
            // The persona id, not the slug: nothing a request carried reaches the log line.
            logger.LogWarning(ex, "Audit write failed for {Action} on persona {PersonaId}; the edit was committed.", action, personaId);
        }
    }
}
