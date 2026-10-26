using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Ops.Mongo;

/// <summary>
/// One process boot (#2297), appended to the <c>process_boots</c> collection by
/// <see cref="Data.Configuration.ProcessBootRecorder"/> and never updated.
///
/// <para>
/// <b>Append-only, unlike <c>effective_configuration</c>.</b> That collection answers "what is
/// this process running now", so a boot overwrites it; this one answers "when did the version
/// change", which needs the history. Whether a boot changed the version is decided once, at
/// write time, against the process's previous versioned boot (<see cref="VersionChanged"/>), so
/// the deploy read is an index range over a handful of rows rather than a scan of every restart
/// — an ingestor running <c>RunOnce</c> boots many times a day.
/// </para>
/// </summary>
public sealed class ProcessBootDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The booting host — <c>Api</c> or <c>Ingestor</c>.</summary>
    [BsonElement("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// What this container of the process does, when one image runs as several containers:
    /// the ingestor's <c>Job:Mode</c> (fetch workers, aggregate lane). Null for the Api. Part of
    /// the process identity, so two lanes booting the same release are each compared to their
    /// own previous boot.
    /// </summary>
    [BsonElement("role")]
    public string? Role { get; set; }

    /// <summary>The host environment name the process booted under.</summary>
    [BsonElement("environment")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// The release this boot runs (<c>AppVersion</c>, set by the deploy). Null on a boot that was
    /// not given one — a local run, a manual redeploy — which is recorded but never a deploy.
    /// </summary>
    [BsonElement("version")]
    public string? Version { get; set; }

    /// <summary>The version of this process's previous versioned boot, null on its first one.</summary>
    [BsonElement("previousVersion")]
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// True when this boot runs a different version from the process's previous versioned boot
    /// (or is its first one): a deploy. False for a restart, which is what the partial TTL
    /// expires.
    /// </summary>
    [BsonElement("versionChanged")]
    public bool VersionChanged { get; set; }

    [BsonElement("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; }
}
