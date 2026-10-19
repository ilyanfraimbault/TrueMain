using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Ops.Mongo;

/// <summary>
/// One pipeline iteration, persisted in the <c>process_iterations</c> collection (#2124).
/// A header, not a record of the pass: its runs stay in <c>process_runs</c>, and this
/// document only exists so the admin can page iterations off an index. It is upserted
/// whenever an iteration-stamped run is written, so an iteration has a header exactly
/// when it has a run, the way the grouping it replaces defined one.
/// </summary>
public sealed class ProcessIterationDocument
{
    /// <summary>The iteration id, stored as a string like <see cref="ProcessRunDocument.IterationId"/>.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>The earliest start among the pass's runs. Also the TTL field.</summary>
    [BsonElement("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; }

    /// <summary>The <c>Job:Mode</c> the pass ran under, or null for passes recorded before #1362.</summary>
    [BsonElement("jobMode")]
    [BsonIgnoreIfNull]
    public string? JobMode { get; set; }
}
