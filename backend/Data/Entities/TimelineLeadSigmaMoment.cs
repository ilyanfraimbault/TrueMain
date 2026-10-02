namespace Data.Entities;

/// <summary>
/// Running variance moments of the per-minute "gold/damage lead vs lane opponent"
/// across the whole tracked population on a queue, one row per (queue, patch,
/// minute). Replaces the per-request <c>STDDEV_SAMP</c> full scan over the raw
/// timeline snapshots (#525 follow-up): the powerspike read reconstructs the
/// per-minute spread σ from these additive moments —
/// <c>σ² = (SumSq − Sum²/N) / (N − 1)</c> — folding patches in the requested scope
/// (variance moments are additive, so "all patches" is just their sum). Add-only
/// per patch, so aged-out patches stay frozen exactly like the sibling lead totals.
/// </summary>
public class TimelineLeadSigmaMoment
{
    public Guid Id { get; set; }

    public int QueueId { get; set; }

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Canonical minute mark (1..30).</summary>
    public int IntervalMinute { get; set; }

    /// <summary>Count of participant-vs-opponent samples folded into this row.</summary>
    public long N { get; set; }

    public double SumGold { get; set; }

    public double SumSqGold { get; set; }

    public double SumDmg { get; set; }

    public double SumSqDmg { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
