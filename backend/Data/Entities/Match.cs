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

    /// <summary>
    /// The canonical <c>major.minor</c> patch of <see cref="GameVersion"/>, computed by
    /// the database as a stored generated column so it can be indexed (#1368).
    /// <see cref="GameVersion"/> holds the full Riot version ("16.17.700.9993"), which
    /// every champion read used to narrow with an unindexable
    /// <c>LIKE '16.17.%'</c> — a single-threaded scan of the whole table now that
    /// parallel query is off (#589).
    /// </summary>
    /// <remarks>
    /// The generating expression mirrors <c>Core.Lol.Patches.PatchVersion.TryParse</c>
    /// followed by <c>ToMajorMinor()</c>: the first two dot-separated, whitespace-trimmed,
    /// non-empty segments, each re-rendered as an integer ("16.04.5" → "16.4"), and
    /// <see langword="null"/> whenever that parse fails — the same "no patch" answer the
    /// C# rule gives. Never assigned from code: Postgres computes it on write, EF maps it
    /// read-only.
    /// </remarks>
    public string? Patch { get; private set; }

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
    /// Set once this match has been folded into <see cref="ChampionMatchupStat"/>
    /// (#811) — game counters and, since #1445, the 15-minute lane verdict in the same
    /// pass. Gates the incremental aggregation (each match is aggregated exactly once)
    /// the same way <see cref="PowerspikeAggregated"/> does. One flag rather than two:
    /// the lane counters had their own until #1445, and a match folded on one side
    /// before its elo bracket was stamped and on the other after it split its two halves
    /// across two rows. Dies with the match on retention, so an aged-out patch's
    /// aggregate rows simply freeze.
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
    /// Whether this match has been folded into <c>champion_profile_stats</c> (#1449), the
    /// same one-fold-per-match gate as <see cref="SynergyAggregated"/>. Ships
    /// <c>false</c> everywhere like the synergy flag, not backfilled like
    /// <see cref="BansAggregated"/>: the table is created empty, and a pre-#1448 match
    /// simply contributes nothing when folded — its participants carry no context
    /// fields — so folding the retained history once costs a scan and corrupts nothing.
    /// </summary>
    public bool ProfileAggregated { get; set; }

    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
}
