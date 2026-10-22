using System.ComponentModel.DataAnnotations;

namespace Ingestor.Options;

/// <summary>
/// Resource backpressure on the match-fetch workers (#1459): host thresholds below which the
/// workers stop claiming until the resource recovers. Ingestion volume has taken the stack
/// down three times without warning — an OOM kill (#600), <c>/dev/shm</c> (#589, memory too)
/// and a full disk (#680) — so the ramp brakes on its own before those walls.
/// </summary>
/// <remarks>
/// Memory and disk only, the two resources behind the past outages; CPU is out of scope until a
/// real CPU problem is observed (product decision on #1459, 2026-10-05). A threshold degrades
/// throughput, never correctness: a batch already claimed is fetched and written to the end,
/// and only the next claim waits.
/// </remarks>
public class IngestionBackpressureOptions
{
    public const string SectionName = "IngestionBackpressure";

    /// <summary>Whether the workers check the host's resources before each claim.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The share of the host's memory that must stay available (<c>MemAvailable</c> over
    /// <c>MemTotal</c>, from <c>/proc/meminfo</c>), in percent. Below it, the workers stop
    /// claiming. A container reads the host's figures there, which is the machine the
    /// database shares with it.
    /// </summary>
    [Range(0, 99)]
    public double MinAvailableMemoryPercent { get; set; } = 10;

    /// <summary>
    /// The share of the disk holding <see cref="DiskPath"/> that must stay free, in percent.
    /// Below it, the workers stop claiming.
    /// </summary>
    [Range(0, 99)]
    public double MinFreeDiskPercent { get; set; } = 15;

    /// <summary>
    /// The path whose disk is watched. The container's root sits on the host's Docker data
    /// root, the same disk as the database volumes on a single-disk host.
    /// </summary>
    [Required]
    public string DiskPath { get; set; } = "/";

    /// <summary>
    /// How far above its threshold a resource must climb before the workers claim again, in
    /// percentage points: without it a reading hovering at the threshold would flap the
    /// workers on and off at every probe.
    /// </summary>
    [Range(0, 50)]
    public double ReleaseMarginPercent { get; set; } = 3;

    /// <summary>How often a throttled worker reads the resources again, in seconds.</summary>
    [Range(1, 3600)]
    public int ProbeIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// A drill: for this many minutes after the first claim of the process, the brake holds as
    /// if a resource were below its threshold, then reads the real resources again. 0 (the
    /// default) is off. What makes an activation <em>and</em> its release observable on a live
    /// environment without filling a disk or starving the host (<c>docs/ingestion-ramp.md</c>).
    /// </summary>
    [Range(0, 1440)]
    public int DrillMinutes { get; set; }
}
