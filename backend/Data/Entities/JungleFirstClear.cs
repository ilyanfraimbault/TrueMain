namespace Data.Entities;

/// <summary>
/// What can actually be measured about a jungler's <b>first clear</b> from the
/// Riot timeline (#1188, correcting #535).
///
/// <para>Riot emits no camp-kill event, and <c>participantFrames</c> are sampled
/// once per <b>minute</b>. A real first clear runs from the 1:30 buff spawn to
/// roughly 3:00–3:15 (median jungle CS is 12 at minute 2 and 20 at minute 3,
/// measured over 286 junglers), so the whole clear is covered by two usable
/// samples. Ordering six camps from two positions is impossible — the previous
/// per-camp sequence was an artifact of crediting one camp per frame, which put
/// a hard 6:00 floor on a clear that really ends at ~3:15.</para>
///
/// <para>So nothing here claims a camp order. What is stored is a start camp
/// (knowable because the jungler waits on it while jungle CS is still 0), the
/// per-minute clear-speed samples, and each sample's position — a coarse
/// "where was he at 2:00" trail, never a named route.</para>
/// </summary>
public class JungleFirstClear
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    /// <summary>
    /// The camp the jungler opened on — the <c>Core.Lol.Map.JungleCamp</c> enum
    /// name at the last frame where jungle CS was still 0. Buffs spawn at 1:30,
    /// so at the 1:00 frame the jungler is standing on (or leashing) the camp he
    /// starts. Null when no such frame exists or the position maps to no camp.
    /// </summary>
    public string? StartCamp { get; set; }

    /// <summary>
    /// Per-minute clear-speed samples across the first-clear window, ascending by
    /// timestamp. Stored compactly as JSONB (mirrors
    /// <c>MatchParticipant.ItemEvents</c>/<c>SkillEvents</c>).
    /// </summary>
    public List<JungleClearSample> Samples { get; set; } = new();

    /// <summary>
    /// The first frame timestamp (ms) at which the jungler's jungle CS reached a
    /// full clear's worth of monsters
    /// (<c>Core.Lol.Map.JungleCamps.FullClearJungleCs</c>). Minute resolution, so
    /// this reads as "full clear by 3:00", not an exact instant. Null when the
    /// jungler never got there inside the window.
    /// </summary>
    public int? FullClearTimeMs { get; set; }
}

/// <summary>One per-minute sample of a jungler's clear: how far along, and where.</summary>
public class JungleClearSample
{
    /// <summary>Frame timestamp (ms since game start).</summary>
    public int TimestampMs { get; set; }

    /// <summary>Cumulative jungle monsters killed at this frame.</summary>
    public int JungleCs { get; set; }

    /// <summary>Map position at this frame — a sampled location, not a camp claim.</summary>
    public int X { get; set; }

    public int Y { get; set; }
}
