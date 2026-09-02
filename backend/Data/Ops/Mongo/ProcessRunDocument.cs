using Data.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Ops.Mongo;

/// <summary>
/// One recorded ingestor process run, persisted in the <c>process_runs</c>
/// collection. Written by the Ingestor's <c>ProcessRunRecorder</c> (a Running row
/// at start, finalised in place on completion, heartbeat-refreshed while in
/// flight) and read by the admin process panels (<c>/ops/process-runs</c>,
/// <c>/ops/process-iterations</c>, <c>/ops/pipeline-health</c>,
/// <c>/ops/stats/aggregations</c>).
/// </summary>
/// <remarks>
/// Moved out of Postgres: process runs are operator-facing observability with no
/// relational joins, exactly what the Mongo observability store (logs, crashes,
/// metrics) holds — SQL keeps the TrueMain game/ingestion data. The id is the
/// recorder-generated <see cref="Guid"/> stored as a plain string (readable in the
/// shell, trivial for the one-shot SQL→Mongo backfill), the status is the
/// <see cref="ProcessRunStatus"/> name for the same reason, and a native TTL index
/// on <see cref="StartedAtUtc"/> bounds the collection — retention Postgres never
/// had for this table.
/// </remarks>
public sealed class ProcessRunDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>
    /// Groups every run written during one full pass of the ingestor pipeline
    /// under a shared id, so the admin can render each iteration as a chain with
    /// its per-process outcomes. Null for runs recorded outside a pipeline pass
    /// (and for legacy rows that predate per-iteration grouping).
    /// </summary>
    [BsonElement("iterationId")]
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? IterationId { get; set; }

    [BsonElement("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// The <c>JobMode</c> the pass was running, or null for runs recorded before this
    /// was captured. Since #1362 a pass covers one lane of the pipeline rather than all
    /// of it, so without this a reader cannot tell a complete fetch-lane pass from a
    /// full pass that stopped halfway.
    /// </summary>
    [BsonElement("jobMode")]
    [BsonIgnoreIfNull]
    public string? JobMode { get; set; }

    /// <summary>When the run started. Also the TTL field.</summary>
    [BsonElement("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// When the run finished. Mirrors <see cref="StartedAtUtc"/> while the run is
    /// still <see cref="ProcessRunStatus.Running"/>, so an in-flight row reads as
    /// zero-duration until it completes.
    /// </summary>
    [BsonElement("finishedAtUtc")]
    public DateTime FinishedAtUtc { get; set; }

    [BsonElement("durationMs")]
    public int DurationMs { get; set; }

    /// <summary>The <see cref="ProcessRunStatus"/> name (e.g. "Success").</summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public ProcessRunStatus Status { get; set; }

    [BsonElement("error")]
    [BsonIgnoreIfNull]
    public string? Error { get; set; }

    [BsonElement("host")]
    [BsonIgnoreIfNull]
    public string? Host { get; set; }

    /// <summary>
    /// Liveness signal for an in-flight run: stamped at start and refreshed
    /// periodically while the process runs. Read queries treat a Running row whose
    /// heartbeat has gone stale (or is null) as <see cref="ProcessRunStatus.Abandoned"/>.
    /// </summary>
    [BsonElement("lastHeartbeatAtUtc")]
    [BsonIgnoreIfNull]
    public DateTime? LastHeartbeatAtUtc { get; set; }

    /// <summary>
    /// The run's summary counters as raw JSON text (the exact payload
    /// <c>ProcessRunSummaryJson</c> serialized). Kept as a string rather than a
    /// nested document so the JSON the admin receives is byte-identical to what the
    /// recorder wrote — no BSON→JSON round-trip to subtly reshape numbers.
    /// </summary>
    [BsonElement("summaryJson")]
    [BsonIgnoreIfNull]
    public string? SummaryJson { get; set; }
}
