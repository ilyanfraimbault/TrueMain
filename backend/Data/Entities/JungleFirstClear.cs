namespace Data.Entities;

/// <summary>
/// A jungler's reconstructed <b>first clear</b> for one match (issue #535): the
/// ordered sequence of camps cleared and the timing of each, derived at ingestion
/// from the in-memory per-minute <c>participantFrames</c> (positions +
/// <c>jungleMinionsKilled</c>). Riot emits no "camp killed" event, so the order is
/// inferred from the per-minute position trail and is reliable only for the first
/// clear (~1 camp/min) — intra-minute timing is interpolated, not exact. Only this
/// compact derived result is persisted; the raw positions are not.
/// </summary>
public class JungleFirstClear
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    /// <summary>
    /// Ordered camps of the first clear, each with the frame timestamp (ms) at
    /// which it was detected as done. Stored compactly as JSONB (mirrors
    /// <c>MatchParticipant.ItemEvents</c>/<c>SkillEvents</c>).
    /// </summary>
    public List<JungleClearStep> Steps { get; set; } = new();

    /// <summary>
    /// Timestamp (ms) at which the full first clear completed — the frame where the
    /// last first-clear camp was detected as done. Null if the jungler never
    /// completed a full clear within the timeline.
    /// </summary>
    public int? FullClearTimeMs { get; set; }
}

/// <summary>One camp of a first clear and when it was detected as cleared.</summary>
public class JungleClearStep
{
    /// <summary>
    /// The camp's name (the <c>Core.Lol.Map.JungleCamp</c> enum name, e.g.
    /// "BlueGromp"). Stored as a string so the Data layer stays decoupled from the
    /// Core map geometry and the JSONB payload is self-describing.
    /// </summary>
    public string Camp { get; set; } = string.Empty;

    /// <summary>Frame timestamp (ms, minute resolution) the camp was detected as done.</summary>
    public int TimestampMs { get; set; }
}
