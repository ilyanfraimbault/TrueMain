namespace Data.Entities;

/// <summary>
/// A participant's game state captured at a fixed minute mark (5/10/15/20/30) of
/// a match, sourced from the Riot timeline. Stores raw per-interval values; the
/// "lead vs lane opponent" is computed at read time by joining the opposing
/// teamPosition for the same match + interval (see issue #525). End-of-game
/// totals are not duplicated here — they already live on <see cref="MatchParticipant"/>.
/// </summary>
public class MatchParticipantTimelineSnapshot
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    /// <summary>Canonical minute mark this snapshot represents (5, 10, 15, 20, 30).</summary>
    public int IntervalMinute { get; set; }

    /// <summary>Actual timestamp of the source frame, in milliseconds.</summary>
    public int TimestampMs { get; set; }

    public int TotalGold { get; set; }

    public int MinionsKilled { get; set; }

    public int JungleMinionsKilled { get; set; }

    public int Level { get; set; }

    public int Xp { get; set; }

    /// <summary>Champion kills by this participant up to <see cref="TimestampMs"/>.</summary>
    public int Kills { get; set; }

    public int DamageToChampions { get; set; }

    /// <summary>Wards placed by this participant up to <see cref="TimestampMs"/> (vision proxy).</summary>
    public int WardsPlaced { get; set; }

    /// <summary>
    /// Enemy wards cleared by this participant up to <see cref="TimestampMs"/>. Counts every
    /// WARD_KILL with a killer, which includes vision-plant (Scryer's Bloom) destructions —
    /// accepted as part of the vision proxy rather than filtered.
    /// </summary>
    public int WardsKilled { get; set; }
}
