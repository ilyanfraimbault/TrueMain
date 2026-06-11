using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Logging.Mongo;

/// <summary>
/// A single diagnostic log record persisted in the <c>logs</c> collection.
/// Written by <see cref="MongoLogSink"/> draining the bounded channel and read
/// back by <see cref="MongoLogQuery"/> for the admin <c>/ops/logs</c> panel.
/// Mirrors the fields the old Postgres <c>LogEntry</c> exposed so the read
/// contract is unchanged.
/// </summary>
/// <remarks>
/// A native TTL index on <see cref="TimestampUtc"/> enforces retention (see
/// <see cref="MongoLogContext"/>), replacing the never-built
/// <c>LogRetentionProcess</c> the Postgres entity carried as a TODO.
/// </remarks>
public sealed class MongoLogDocument
{
    /// <summary>
    /// Server-generated ObjectId. Surfaced as a 24-char hex string on
    /// <c>/ops/logs</c> (the read model's <c>id</c> is a string, and the admin UI
    /// already accepts <c>number | string</c>).
    /// </summary>
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("timestampUtc")]
    public DateTime TimestampUtc { get; set; }

    /// <summary>The <c>Microsoft.Extensions.Logging.LogLevel</c> name (e.g. "Warning").</summary>
    [BsonElement("level")]
    public string Level { get; set; } = string.Empty;

    /// <summary>The logger category (typically the source type's full name).</summary>
    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("exception")]
    [BsonIgnoreIfNull]
    public string? Exception { get; set; }

    [BsonElement("processName")]
    [BsonIgnoreIfNull]
    public string? ProcessName { get; set; }

    [BsonElement("host")]
    [BsonIgnoreIfNull]
    public string? Host { get; set; }

    [BsonElement("eventId")]
    [BsonIgnoreIfNull]
    public int? EventId { get; set; }
}
