using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Metrics.Mongo;

/// <summary>
/// How many candidates sat in one status, on one platform, at one hour (#1403).
/// Written by the Ingestor's candidate-stock snapshot step and read back by the
/// admin candidates panel to chart the funnel's <em>level</em> over time.
///
/// <para>
/// <b>Why this is recorded rather than derived.</b> The candidate funnel (#1024)
/// measures flow — how much moved per period — from the run summaries, and the
/// status list on the same page measures the stock, but only right now. The stock
/// over time cannot be reconstructed after the fact: <c>main_candidates</c> carries
/// <c>DiscoveredAtUtc</c>, <c>ScoredAtUtc</c> and <c>ValidatedAtUtc</c> but no
/// <c>QueuedAtUtc</c> (so Scored and Queued are indistinguishable in the past), and
/// pruning and demotion delete rows outright, so any reconstruction would be
/// survivor-biased and would understate every past level. Forward-only, like the
/// storage snapshots (#925) it is modelled on.
/// </para>
///
/// <para>
/// <b>Hourly, last write wins.</b> The document is keyed on
/// <see cref="SnapshotHourUtc"/> rather than the wall clock, so the pipeline running
/// back-to-back (prod: <c>RunOnce</c> + <c>restart: unless-stopped</c>) refreshes the
/// hour's reading instead of appending a point per run. The hour, not the day, because
/// two of the six statuses are transient by construction — Scoring drains the whole
/// <c>New</c> backlog each run, and <c>Processing</c> is a claim held for the length of
/// one match-ingestion pass — so a daily reading would show both at 0 forever and say
/// nothing about whether scoring is keeping up or leases are being reaped (#1344).
/// </para>
/// </summary>
public sealed class CandidateStockSnapshotDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The snapshot's hour, truncated to the top of the hour UTC. Also the TTL field.</summary>
    [BsonElement("snapshotHourUtc")]
    public DateTime SnapshotHourUtc { get; set; }

    /// <summary>
    /// The platform the count is scoped to. Part of the key rather than pre-summed:
    /// the read side sums it away today, but the per-region split is the whole subject
    /// of the ingestion-imbalance work (#1149, #1150), and a stock that was summed at
    /// write time could never be broken back down.
    /// </summary>
    [BsonElement("platformId")]
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>
    /// The <c>MainCandidateStatus</c> name (<c>New</c>, <c>Scored</c>, …), stored as
    /// text rather than as the enum's integer: this collection is read by an ops panel
    /// and by an operator with a mongo shell, and a document reading <c>status: 2</c>
    /// forces both to know the enum's numbering.
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Rows in that status, on that platform, at that instant. An exact
    /// <c>count(*)</c>, unlike the storage snapshot's planner estimate — the whole
    /// group-by is one index-only scan (measured at ~190 ms over 745k rows in prod).
    /// </summary>
    [BsonElement("count")]
    public long Count { get; set; }

    /// <summary>Wall-clock time the reading was taken, for "last updated" display.</summary>
    [BsonElement("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; set; }
}
