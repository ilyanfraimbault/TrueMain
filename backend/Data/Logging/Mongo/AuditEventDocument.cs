using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Logging.Mongo;

/// <summary>
/// A single operator-action audit event persisted in the <c>audit_events</c>
/// collection. Written synchronously and losslessly by <see cref="MongoAuditLog"/>
/// from intentional operator write actions (e.g. seeding a main), never through
/// the lossy batched diagnostic-log channel.
/// </summary>
/// <remarks>
/// Unlike the diagnostic <c>logs</c> collection, <c>audit_events</c> has no TTL
/// index: operator actions are retained indefinitely (resolved open question,
/// #416).
/// </remarks>
public sealed class AuditEventDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>Who performed the action (e.g. an operator/admin identity or "system").</summary>
    [BsonElement("actor")]
    public string Actor { get; set; } = string.Empty;

    /// <summary>What was done, as a stable verb-phrase key (e.g. "seed_account").</summary>
    [BsonElement("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>The kind of entity acted on (e.g. "SeedRequest").</summary>
    [BsonElement("targetType")]
    [BsonIgnoreIfNull]
    public string? TargetType { get; set; }

    /// <summary>The acted-on entity's identifier (e.g. the SeedRequest id).</summary>
    [BsonElement("targetId")]
    [BsonIgnoreIfNull]
    public string? TargetId { get; set; }

    /// <summary>
    /// Free-form structured context for the action (e.g. the Riot ID + platform
    /// of a seeded account). Stored as a nested document; absent when empty.
    /// </summary>
    [BsonElement("metadata")]
    [BsonIgnoreIfNull]
    public Dictionary<string, string>? Metadata { get; set; }

    [BsonElement("timestampUtc")]
    public DateTime TimestampUtc { get; set; }
}
