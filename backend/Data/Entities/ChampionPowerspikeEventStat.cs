namespace Data.Entities;

/// <summary>
/// Pre-aggregated occurrence record for a champion powerspike event — a level
/// milestone (6/11/16) or the first purchase of an item — one row per
/// (champion, position, patch, event). Carries only the game count and the sum of
/// the per-game event minutes, so the read derives the mean event minute
/// (<c>SumEventMinute / Games</c>) and evaluates the spike as the curvature of the
/// aggregated power curve around it. Items store every purchased item; the read
/// intersects them with the champion's dominant build. Replaces the per-request
/// scan over the raw timeline snapshots.
/// </summary>
public class ChampionPowerspikeEventStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Lane of the champion side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>"level" or "item".</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Champion level (6/11/16) for "level", item id for "item".</summary>
    public int RefId { get; set; }

    public int Games { get; set; }

    /// <summary>Sum of the per-game event minutes; divided by Games for the mean.</summary>
    public long SumEventMinute { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
