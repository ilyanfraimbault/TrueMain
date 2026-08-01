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

    /// <summary>
    /// Set once this match has been folded into the champion synergy aggregates
    /// (#922), the same one-fold-per-match gate as <see cref="MatchupLeadAggregated"/>.
    /// Unlike that flag, its migration deliberately does NOT backfill existing rows
    /// to <see langword="true"/>: the synergy tables are introduced empty, so every
    /// retained match still has to be folded exactly once. (The rule of thumb — a new
    /// incremental flag must be backfilled to true — protects an aggregate a full
    /// recompute had already populated; there is none here.) Dies with the match on
    /// retention, so an aged-out patch's synergy rows simply freeze.
    /// </summary>
    public bool SynergyAggregated { get; set; }

    /// <summary>
    /// Set once this match has been folded into the champion ban aggregates (#920),
    /// the same one-fold-per-match gate as <see cref="SynergyAggregated"/>. Its
    /// migration backfills every existing row to <see langword="true"/> — and here
    /// that is not the double-counting guard it was for
    /// <see cref="MatchupLeadAggregated"/>, but a correctness one: bans could not be
    /// backfilled (Riot payloads are not kept), so a match ingested before #920 has
    /// no <see cref="MatchBan"/> rows at all. Folding it would add one to the ban
    /// denominator while contributing no bans, deflating every champion's rate for
    /// as long as those matches are retained. Dies with the match on retention, so
    /// an aged-out patch's ban rows simply freeze.
    /// </summary>
    public bool BansAggregated { get; set; }

    /// <summary>
    /// Set once this match has been folded into the lane-outcome counters on
    /// <see cref="ChampionMatchupStat"/> (#919). A flag of its own rather than reusing
    /// <see cref="MatchupLeadAggregated"/>, which was backfilled to true in #811 and so
    /// would have excluded every existing match. Shipping this one false lets the fold
    /// drain the retained window immediately: unlike the bans of #920, the source data
    /// (the 15-minute timeline snapshots) still exists for every retained match, since
    /// snapshot pruning keeps the canonical marks. Dies with the match on retention, so
    /// an aged-out patch's lane counters freeze like everything else.
    /// </summary>
    public bool LaneOutcomeAggregated { get; set; }

    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
}
