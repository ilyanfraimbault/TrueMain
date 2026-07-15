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
    /// Set once this match has been folded into the champion powerspike aggregates
    /// (#694). Gates the incremental aggregation (each match is aggregated exactly
    /// once) and the snapshot pruning (only a flagged match's intermediate-minute
    /// timeline snapshots may be dropped). Dies with the match on retention, so an
    /// aged-out patch's aggregate rows simply freeze.
    /// </summary>
    public bool PowerspikeAggregated { get; set; }

    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
}
