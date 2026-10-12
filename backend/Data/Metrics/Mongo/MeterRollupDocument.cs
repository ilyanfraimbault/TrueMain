using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// One minute of one instrument's measurements for one tag set, persisted in the
/// <c>meter_rollups</c> collection (#1636). Written by the Ingestor's meter exporter, which
/// listens to its own <c>TrueMain.Ingestor</c> meter, and read back by the admin Riot API tab.
/// </summary>
/// <remarks>
/// The shape is the instrument's, not a Riot call's: a counter folds into <see cref="Count"/>
/// measurements adding up to <see cref="Sum"/>, a histogram into the same two plus
/// <see cref="Max"/>. Nothing is pre-aggregated per window; every window the admin reads is
/// a sum over minutes, like the Riot call rollups. Unique on
/// <c>(bucketStartUtc, meter, instrument, seriesKey)</c>, so an <c>$inc</c>-upsert from two
/// ingestor containers lands on one document.
/// </remarks>
public sealed class MeterRollupDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The minute the measurements fall in, UTC.</summary>
    [BsonElement("bucketStartUtc")]
    public DateTime BucketStartUtc { get; set; }

    /// <summary>The meter the instrument belongs to, e.g. <c>TrueMain.Ingestor</c>.</summary>
    [BsonElement("meter")]
    public string Meter { get; set; } = string.Empty;

    /// <summary>The instrument name, e.g. <c>ingestor.riot.ratelimit.wait</c>.</summary>
    [BsonElement("instrument")]
    public string Instrument { get; set; } = string.Empty;

    /// <summary>
    /// The tag set in one canonical string — <c>key=value</c> pairs sorted by key, joined by
    /// <c>|</c> — so the unique index can key on it. <see cref="Tags"/> carries the same pairs
    /// for reading.
    /// </summary>
    [BsonElement("seriesKey")]
    public string SeriesKey { get; set; } = string.Empty;

    /// <summary>The measurement's tags, values stringified.</summary>
    [BsonElement("tags")]
    public Dictionary<string, string> Tags { get; set; } = [];

    /// <summary><c>counter</c> or <c>histogram</c>.</summary>
    [BsonElement("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The instrument's declared unit (<c>ms</c>, <c>{failure}</c>), when it has one.</summary>
    [BsonElement("unit")]
    [BsonIgnoreIfNull]
    public string? Unit { get; set; }

    /// <summary>The instrument's declared description, so the reader needs no copy of it.</summary>
    [BsonElement("description")]
    [BsonIgnoreIfNull]
    public string? Description { get; set; }

    /// <summary>Measurements recorded in the minute.</summary>
    [BsonElement("count")]
    public long Count { get; set; }

    /// <summary>Sum of the measured values: the counter's increment, the histogram's total.</summary>
    [BsonElement("sum")]
    public double Sum { get; set; }

    /// <summary>The largest single measurement in the minute.</summary>
    [BsonElement("max")]
    public double Max { get; set; }

    /// <summary>When the latest measurement folded into this rollup was recorded, UTC.</summary>
    [BsonElement("lastRecordedAtUtc")]
    public DateTime LastRecordedAtUtc { get; set; }

    /// <summary>Producing host (e.g. <c>Ingestor</c>), stamped from configuration.</summary>
    [BsonElement("processName")]
    [BsonIgnoreIfNull]
    public string? ProcessName { get; set; }
}
