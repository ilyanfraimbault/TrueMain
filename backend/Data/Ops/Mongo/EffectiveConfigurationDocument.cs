using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Ops.Mongo;

/// <summary>
/// One process's effective configuration as published at its own boot (#1034), persisted in
/// the <c>effective_configuration</c> collection.
///
/// <para>
/// It exists because the Api cannot see the Ingestor's options: production sets
/// <c>Discovery__MaxAccountsPerPlatformPerRun</c>, <c>Scoring__TopNPerPlatform</c>,
/// <c>Harvest__MaxCandidatesPerRun</c> and <c>MatchIngestion__BatchSize</c> on the ingestor
/// container alone, and the option classes live in an assembly the Api does not reference. So
/// the process that binds the values is the one that reports them, and the Api only relays.
/// </para>
///
/// <para>
/// <b>One document per process, overwritten at every boot.</b> Prod runs the ingestor with
/// <c>RunOnce</c> plus <c>restart: unless-stopped</c>, so it boots back-to-back many times a
/// day. Keying the upsert on <see cref="ProcessName"/> alone turns that into "refresh what this
/// process is running" instead of an append-only log nobody would read past the first row. No
/// TTL either: this is functional operator state like <c>seed_requests</c>, and expiring it
/// would blank the page exactly when the ingestor has been down long enough to matter.
/// </para>
/// </summary>
public sealed class EffectiveConfigurationDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The publishing host — <c>Ingestor</c>. Also the upsert key.</summary>
    [BsonElement("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>The host environment name the process booted under.</summary>
    [BsonElement("environment")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>The build it is running, when the assembly carries an informational version.</summary>
    [BsonElement("version")]
    public string? Version { get; set; }

    /// <summary>
    /// When the process bound these values. The page states its age rather than hiding it: a
    /// snapshot older than the last compose change is still what that process is running.
    /// </summary>
    [BsonElement("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; set; }

    [BsonElement("sections")]
    public List<EffectiveConfigurationSectionDocument> Sections { get; set; } = [];
}

/// <summary>One allow-listed section inside a published snapshot.</summary>
public sealed class EffectiveConfigurationSectionDocument
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("values")]
    public List<EffectiveConfigurationValueDocument> Values { get; set; } = [];
}

/// <summary>
/// One published option. Every field is already a rendered string, which is why this is stored
/// as typed BSON rather than as the raw JSON <c>process_runs</c> uses for its summaries: there
/// are no numbers for the BSON round-trip to reshape.
/// </summary>
public sealed class EffectiveConfigurationValueDocument
{
    [BsonElement("key")]
    public string Key { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("value")]
    public string? Value { get; set; }

    [BsonElement("valueLabel")]
    public string? ValueLabel { get; set; }

    /// <summary><c>default</c>, <c>override</c> or <c>derived</c>.</summary>
    [BsonElement("origin")]
    public string Origin { get; set; } = string.Empty;

    /// <summary>Which provider supplied an override, e.g. <c>environment</c>. Null otherwise.</summary>
    [BsonElement("source")]
    public string? Source { get; set; }

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    /// <summary>The consequence sentence when the value is unset and that is visible elsewhere.</summary>
    [BsonElement("notice")]
    public string? Notice { get; set; }
}
