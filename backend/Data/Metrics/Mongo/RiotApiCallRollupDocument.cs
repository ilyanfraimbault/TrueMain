using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// A per-minute rollup of Riot API requests, persisted in the
/// <c>riot_api_call_rollups</c> collection. One document aggregates every HTTP
/// attempt sharing the same <see cref="BucketStartUtc"/> (the call timestamp
/// truncated to the minute), <see cref="Endpoint"/> and <see cref="StatusCode"/>,
/// so thousands of calls/minute collapse to at most
/// <c>endpoints × statusCodes</c> documents. Written by
/// <see cref="RiotApiMetricsSink"/> (which <c>$inc</c>-upserts a drained batch)
/// and read back by <see cref="RiotApiUsageQuery"/> for the admin
/// <c>/ops/riot-usage</c> panel (#93).
/// </summary>
/// <remarks>
/// Retries still count: every physical request increments <see cref="Count"/>,
/// and a retried 429 lands in the 429 status bucket, so the status-code breakdown
/// and the rate-limit picture stay faithful. Replaces the per-call
/// <c>RiotApiCallDocument</c>: storing one document per attempt made the
/// collection (and the panel's whole-window aggregations) scale with raw call
/// volume, pegging Mongo's CPU. A native TTL index on <see cref="BucketStartUtc"/>
/// (see <c>MongoLogContext</c>) bounds retention; a unique index on
/// <c>(bucketStartUtc, endpoint, statusCode)</c> makes the upsert target exactly
/// one document.
/// </remarks>
public sealed class RiotApiCallRollupDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>
    /// The minute the aggregated calls fall in: their timestamp truncated to the
    /// minute (UTC). One minute is finer than the smallest display bin (5 minutes
    /// on the 1-hour window), so every panel window re-bins from these cleanly.
    /// </summary>
    [BsonElement("bucketStartUtc")]
    public DateTime BucketStartUtc { get; set; }

    /// <summary>
    /// Stable, low-cardinality endpoint key (the Riot "method" id, e.g.
    /// <c>match-v5.getMatch</c>) produced by <c>RiotEndpointClassifier</c>. Path
    /// parameters are stripped so calls group cleanly.
    /// </summary>
    [BsonElement("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code shared by every call in this rollup, or <c>0</c> when the
    /// request faulted before a response (timeout, socket error). Status 0 and any
    /// non-2xx/3xx code count as errors in the read query.
    /// </summary>
    [BsonElement("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>Number of attempts aggregated into this rollup.</summary>
    [BsonElement("count")]
    public long Count { get; set; }

    /// <summary>
    /// Sum of per-attempt round-trip latencies (ms) across the rollup. The mean is
    /// derived exactly as <c>SumLatencyMs / Count</c>, so no rounding accumulates.
    /// </summary>
    [BsonElement("sumLatencyMs")]
    public long SumLatencyMs { get; set; }

    /// <summary>
    /// Timestamp of the most recent call folded into this rollup (UTC). Backs the
    /// per-endpoint "last called at" and orders the rate-limit snapshot.
    /// </summary>
    [BsonElement("lastCalledAtUtc")]
    public DateTime LastCalledAtUtc { get; set; }

    /// <summary>HTTP method (always <c>GET</c> today, kept for completeness). Last-seen in the bucket.</summary>
    [BsonElement("method")]
    [BsonIgnoreIfNull]
    public string? Method { get; set; }

    /// <summary>
    /// Routing value (regional host like <c>europe</c> or platform host like
    /// <c>euw1</c>). Last-seen in the bucket. Optional.
    /// </summary>
    [BsonElement("route")]
    [BsonIgnoreIfNull]
    public string? Route { get; set; }

    /// <summary>
    /// Riot <c>X-App-Rate-Limit</c> header (app limit definitions, e.g.
    /// <c>20:1,100:120</c>). Last-seen in the bucket; the read query reads it from
    /// the freshest rollup to show the "current" limit. Optional.
    /// </summary>
    [BsonElement("appRateLimit")]
    [BsonIgnoreIfNull]
    public string? AppRateLimit { get; set; }

    /// <summary>
    /// Riot <c>X-App-Rate-Limit-Count</c> header (current usage per app window,
    /// e.g. <c>1:1,1:120</c>). Last-seen in the bucket. Optional.
    /// </summary>
    [BsonElement("appRateLimitCount")]
    [BsonIgnoreIfNull]
    public string? AppRateLimitCount { get; set; }

    /// <summary>Riot <c>X-Method-Rate-Limit</c> header (per-method limits). Last-seen in the bucket. Optional.</summary>
    [BsonElement("methodRateLimit")]
    [BsonIgnoreIfNull]
    public string? MethodRateLimit { get; set; }

    /// <summary>Riot <c>X-Method-Rate-Limit-Count</c> header (per-method usage). Last-seen in the bucket. Optional.</summary>
    [BsonElement("methodRateLimitCount")]
    [BsonIgnoreIfNull]
    public string? MethodRateLimitCount { get; set; }

    /// <summary>
    /// <c>Retry-After</c> seconds from the freshest 429/503 in the bucket. Present
    /// only when Riot asked the client to back off. Optional.
    /// </summary>
    [BsonElement("retryAfterSeconds")]
    [BsonIgnoreIfNull]
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// Riot <c>X-Rate-Limit-Type</c> on a 429 (<c>application</c> / <c>method</c> /
    /// <c>service</c>). Last-seen in the bucket. Optional.
    /// </summary>
    [BsonElement("rateLimitType")]
    [BsonIgnoreIfNull]
    public string? RateLimitType { get; set; }

    /// <summary>Producing host identifier (e.g. "Ingestor"). Last-seen in the bucket. Optional.</summary>
    [BsonElement("processName")]
    [BsonIgnoreIfNull]
    public string? ProcessName { get; set; }
}
