using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// One minute of one ops route's requests as the Api answered them (#2307), persisted in
/// <c>request_latency_rollups</c>. Written by the Api's request-latency flusher (one
/// <c>$inc</c>-upsert per minute, route and method) and read by the admin Processes →
/// Performance tab, which sums the minutes of its window and reads p50/p95 off the summed
/// <see cref="Histogram"/>.
/// </summary>
public sealed class RequestLatencyRollupDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The minute the requests started in, truncated (UTC).</summary>
    [BsonElement("bucketStartUtc")]
    public DateTime BucketStartUtc { get; set; }

    /// <summary>The route template, e.g. <c>/ops/accounts/{puuid}</c> — never the concrete path.</summary>
    [BsonElement("route")]
    public string Route { get; set; } = string.Empty;

    /// <summary>The HTTP method.</summary>
    [BsonElement("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>Requests answered (or abandoned) in the minute.</summary>
    [BsonElement("count")]
    public long Count { get; set; }

    /// <summary>Sum of their durations in milliseconds, for the mean.</summary>
    [BsonElement("sumMs")]
    public double SumMs { get; set; }

    /// <summary>The slowest one.</summary>
    [BsonElement("maxMs")]
    public double MaxMs { get; set; }

    /// <summary>
    /// Request counts per <see cref="RequestLatencyHistogram"/> bucket, keyed
    /// <see cref="BucketKey"/> (<c>b0</c>, <c>b1</c>…); a bucket no request fell in is absent.
    /// A sub-document rather than an array because an upsert's <c>$inc</c> on
    /// <c>histogram.3</c> would create an object anyway, never an array.
    /// </summary>
    [BsonElement("histogram")]
    public Dictionary<string, long> Histogram { get; set; } = [];

    /// <summary>Requests the client abandoned or that ended cancelled or timed out.</summary>
    [BsonElement("timeouts")]
    public long Timeouts { get; set; }

    /// <summary>Requests answered with a 5xx.</summary>
    [BsonElement("serverErrors")]
    public long ServerErrors { get; set; }

    [BsonElement("lastRecordedAtUtc")]
    public DateTime LastRecordedAtUtc { get; set; }

    /// <summary>The field name of histogram bucket <paramref name="index"/>.</summary>
    public static string BucketKey(int index) => $"b{index}";

    [BsonElement("processName")]
    [BsonIgnoreIfNull]
    public string? ProcessName { get; set; }
}
