using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// A single physical request to the Riot API, persisted in the
/// <c>riot_api_calls</c> collection. One document is written per HTTP attempt
/// (retries included) by <c>RiotApiMetricsHandler</c> in the Ingestor, draining
/// through <see cref="RiotApiMetricsChannel"/> and
/// <see cref="RiotApiMetricsSink"/>, and read back by
/// <see cref="RiotApiUsageQuery"/> for the admin <c>/ops/riot-usage</c> panel
/// (#93).
/// </summary>
/// <remarks>
/// Retries are counted as separate calls deliberately: every physical request
/// consumes Riot rate budget, so a retried 429 must show up in the status-code
/// breakdown and the rate-limit picture. A native TTL index on
/// <see cref="TimestampUtc"/> enforces retention (see <c>MongoLogContext</c>),
/// so the collection stays bounded without a sweeper process.
/// </remarks>
public sealed class RiotApiCallDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("timestampUtc")]
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Stable, low-cardinality endpoint key (the Riot "method" id, e.g.
    /// <c>match-v5.getMatch</c>) produced by <c>RiotEndpointClassifier</c>. Path
    /// parameters (puuids, match ids, queues) are stripped so calls group cleanly.
    /// </summary>
    [BsonElement("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>HTTP method (always <c>GET</c> today, kept for completeness).</summary>
    [BsonElement("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code of the response, or <c>0</c> when the request faulted
    /// before a response was received (timeout, socket error). Status 0 and any
    /// non-2xx/3xx code count as errors in the read query.
    /// </summary>
    [BsonElement("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>Round-trip latency in milliseconds for this single attempt.</summary>
    [BsonElement("latencyMs")]
    public long LatencyMs { get; set; }

    /// <summary>
    /// Routing value the request was sent to (regional host like <c>europe</c> or
    /// platform host like <c>euw1</c>), parsed from the request host. Optional.
    /// </summary>
    [BsonElement("route")]
    [BsonIgnoreIfNull]
    public string? Route { get; set; }

    /// <summary>
    /// Riot <c>X-App-Rate-Limit</c> response header (the app limit definitions,
    /// e.g. <c>20:1,100:120</c>). Same on every response, so the read query reads
    /// it from the most recent call to show the "current" limit. Optional.
    /// </summary>
    [BsonElement("appRateLimit")]
    [BsonIgnoreIfNull]
    public string? AppRateLimit { get; set; }

    /// <summary>
    /// Riot <c>X-App-Rate-Limit-Count</c> response header (current usage against
    /// each app window, e.g. <c>1:1,1:120</c>). Optional.
    /// </summary>
    [BsonElement("appRateLimitCount")]
    [BsonIgnoreIfNull]
    public string? AppRateLimitCount { get; set; }

    /// <summary>Riot <c>X-Method-Rate-Limit</c> response header (per-method limits). Optional.</summary>
    [BsonElement("methodRateLimit")]
    [BsonIgnoreIfNull]
    public string? MethodRateLimit { get; set; }

    /// <summary>Riot <c>X-Method-Rate-Limit-Count</c> response header (per-method usage). Optional.</summary>
    [BsonElement("methodRateLimitCount")]
    [BsonIgnoreIfNull]
    public string? MethodRateLimitCount { get; set; }

    /// <summary>
    /// <c>Retry-After</c> seconds returned with a 429 (or service 503). Present
    /// only when Riot asked the client to back off. Optional.
    /// </summary>
    [BsonElement("retryAfterSeconds")]
    [BsonIgnoreIfNull]
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// Riot <c>X-Rate-Limit-Type</c> header on a 429 (<c>application</c> /
    /// <c>method</c> / <c>service</c>), identifying which bucket was exceeded.
    /// Optional.
    /// </summary>
    [BsonElement("rateLimitType")]
    [BsonIgnoreIfNull]
    public string? RateLimitType { get; set; }

    /// <summary>Producing host identifier (e.g. "Ingestor"). Optional.</summary>
    [BsonElement("processName")]
    [BsonIgnoreIfNull]
    public string? ProcessName { get; set; }
}
