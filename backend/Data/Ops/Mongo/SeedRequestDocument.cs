using Data.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Ops.Mongo;

/// <summary>
/// A request to bring a single account into the pipeline by its Riot ID, persisted
/// in the <c>seed_requests</c> collection. The API records a Pending document
/// (admin "add a main" panel, #410); the Ingestor's <c>ManualSeedProcess</c> claims
/// it atomically (Pending→Resolving), resolves the PUUID via account-v1, upserts
/// the account and its candidates, and stamps the terminal status.
/// </summary>
/// <remarks>
/// Moved out of Postgres with the rest of the admin-portal data: the queue is
/// created by and surfaced to the admin only, joins nothing relationally (the
/// candidate detail read looks it up by resolved PUUID in a separate query), and
/// its claim semantics map onto a single-document atomic update. Ids and statuses
/// are stored as strings for shell readability and a trivial one-shot backfill.
/// No TTL: seed requests are functional history, small in volume.
/// </remarks>
public sealed class SeedRequestDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>The Riot ID game name, as submitted (trimmed).</summary>
    [BsonElement("gameName")]
    public string GameName { get; set; } = string.Empty;

    /// <summary>The Riot ID tag line, as submitted (trimmed, without the leading '#').</summary>
    [BsonElement("tagLine")]
    public string TagLine { get; set; } = string.Empty;

    /// <summary>The platform the account belongs to (e.g. "EUW1"); a <c>PlatformRoute</c> name.</summary>
    [BsonElement("platformId")]
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>The <see cref="SeedRequestStatus"/> name (e.g. "Pending").</summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public SeedRequestStatus Status { get; set; }

    /// <summary>Failure detail when <see cref="Status"/> is Failed; else null.</summary>
    [BsonElement("error")]
    [BsonIgnoreIfNull]
    public string? Error { get; set; }

    [BsonElement("requestedAtUtc")]
    public DateTime RequestedAtUtc { get; set; }

    /// <summary>When the Ingestor reached a terminal state (Ingested/Failed); null while unprocessed.</summary>
    [BsonElement("processedAtUtc")]
    [BsonIgnoreIfNull]
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>The PUUID account-v1 resolved for this Riot ID; null until ingested.</summary>
    [BsonElement("resolvedPuuid")]
    [BsonIgnoreIfNull]
    public string? ResolvedPuuid { get; set; }

    /// <summary>The upserted <c>RiotAccount</c>'s id; null until ingested.</summary>
    [BsonElement("resolvedRiotAccountId")]
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? ResolvedRiotAccountId { get; set; }
}
