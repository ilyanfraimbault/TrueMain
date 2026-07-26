namespace Data.Entities;

public class Match
{
    // A match row is immutable identity-wise once created (only the ingest/aggregate
    // bool flags below are ever flipped), so the id and platform are required + init.
    public required string Id { get; init; }

    public required string PlatformId { get; init; }

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

    /// <summary>
    /// Set once this match's intermediate-minute timeline snapshots have been pruned
    /// down to the canonical marks (5/10/15/20/30) by retention (#694). The dense
    /// per-minute grid only feeds the one-shot powerspike aggregation, so once a match
    /// is <see cref="PowerspikeAggregated"/> its extra minutes are dead weight and get
    /// dropped exactly once — this flag keeps retention from re-scanning a pruned match.
    /// </summary>
    public bool TimelineSnapshotsPruned { get; set; }

    /// <summary>
    /// Set once this match has been folded into the champion matchup/lead aggregates
    /// (#811). Gates the incremental aggregation (each match is aggregated exactly
    /// once) the same way <see cref="PowerspikeAggregated"/> does. Dies with the match
    /// on retention, so an aged-out patch's aggregate rows simply freeze.
    /// </summary>
    public bool MatchupLeadAggregated { get; set; }

    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
}
