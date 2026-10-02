namespace Data.Entities;

public class Match
{
    public string Id { get; set; } = string.Empty;

    public string PlatformId { get; set; } = string.Empty;

    public int QueueId { get; set; }

    public int MapId { get; set; }

    public string GameMode { get; set; } = string.Empty;

    public string GameType { get; set; } = string.Empty;

    public DateTime GameStartTimeUtc { get; set; }

    public int GameDurationSeconds { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public bool TimelineIngested { get; set; }

    /// <summary>
    /// Set once the match's timeline has been folded into the powerspike/lead
    /// aggregates. Guards against double-counting when the same match is ingested
    /// from more than one tracked account (the accumulators are add-only), claimed
    /// atomically via a conditional update.
    /// </summary>
    public bool TimelineAggregated { get; set; }

    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
}
