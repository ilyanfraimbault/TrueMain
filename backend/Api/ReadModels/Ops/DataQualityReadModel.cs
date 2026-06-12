namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The data-quality checks the panel can run, each independently listable. The
/// string name is what crosses the API boundary (serialised camelCase via the
/// global policy) and what the <c>issue</c> query filter accepts.
/// </summary>
public enum DataQualityIssueType
{
    /// <summary>
    /// <c>Match.TimelineIngested = false</c> for longer than the staleness
    /// window, i.e. the timeline backfill looks stuck rather than merely
    /// pending. Queue-agnostic.
    /// </summary>
    MissingTimeline,

    /// <summary>
    /// The number of <c>match_participants</c> rows differs from the queue's
    /// expected participant count. Only for queues with a known profile.
    /// </summary>
    WrongParticipantCount,

    /// <summary>
    /// A team is missing one of the five Summoner's Rift lane positions. Only
    /// for lane-based queues (SR 5v5); never fires on ARAM/Arena.
    /// </summary>
    MissingTeamPosition,

    /// <summary>
    /// <c>GameDurationSeconds</c> is 0 — a game with no recorded length is
    /// almost always a remake/ingest glitch. Queue-agnostic.
    /// </summary>
    ZeroDuration,

    /// <summary>
    /// The same champion appears twice on one team — impossible in a real game,
    /// so it signals a duplicated/garbled participant row. Only for lane-based
    /// queues, where a team is a well-defined 5-champion set.
    /// </summary>
    DuplicateChampion
}

/// <summary>
/// A page of flagged matches for the data-quality panel, grouped by issue type.
/// <see cref="Groups"/> carries the (capped) sample of matches per issue plus
/// the full count, so the panel renders "N matches" headers without paging every
/// row. <see cref="Total"/> is the distinct flagged-match count across the
/// active filters (a match flagged by several checks is counted once).
/// </summary>
public sealed record IncompleteMatchesReadModel
{
    public IReadOnlyList<DataQualityIssueGroupReadModel> Groups { get; init; } = [];

    /// <summary>Distinct matches flagged by at least one active check.</summary>
    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>
    /// Matches with <c>TimelineIngested = false</c> are only flagged once older
    /// than this many hours, so the normal recovery backlog isn't reported as
    /// stuck. Echoed back so the panel can label the threshold it's seeing.
    /// </summary>
    public int StaleTimelineThresholdHours { get; init; }
}

/// <summary>One issue type's flagged matches: a capped sample plus the full count.</summary>
public sealed record DataQualityIssueGroupReadModel
{
    /// <summary>The <see cref="DataQualityIssueType"/> name (camelCase on the wire).</summary>
    public string IssueType { get; init; } = string.Empty;

    /// <summary>Total matches flagged by this check across the active filters.</summary>
    public long Count { get; init; }

    /// <summary>Newest-first sample of flagged matches (bounded by the page size).</summary>
    public IReadOnlyList<FlaggedMatchReadModel> Matches { get; init; } = [];
}

/// <summary>A single flagged match row in the list.</summary>
public sealed record FlaggedMatchReadModel
{
    public string MatchId { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public int QueueId { get; init; }

    public DateTime GameStartTimeUtc { get; init; }

    public int GameDurationSeconds { get; init; }

    public bool TimelineIngested { get; init; }

    /// <summary>Actual <c>match_participants</c> row count for this match.</summary>
    public int ParticipantCount { get; init; }

    /// <summary>Expected participant count for the queue, or null if the queue is unknown.</summary>
    public int? ExpectedParticipantCount { get; init; }

    /// <summary>
    /// Every issue this match is flagged by (a match can trip several checks),
    /// as <see cref="DataQualityIssueType"/> names.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>
/// Per-match detail: both teams laid out by position with the gaps identified.
/// Returned by <c>GET /ops/data-quality/match/{id}</c>.
/// </summary>
public sealed record MatchDataQualityDetailReadModel
{
    public string MatchId { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public int QueueId { get; init; }

    public string GameMode { get; init; } = string.Empty;

    public DateTime GameStartTimeUtc { get; init; }

    public int GameDurationSeconds { get; init; }

    public string GameVersion { get; init; } = string.Empty;

    public bool TimelineIngested { get; init; }

    public int ParticipantCount { get; init; }

    public int? ExpectedParticipantCount { get; init; }

    /// <summary>True when the queue has a known profile (count/position rules apply).</summary>
    public bool QueueKnown { get; init; }

    /// <summary>True when <c>TeamPosition</c> is meaningful for this queue.</summary>
    public bool HasLanes { get; init; }

    /// <summary>Issue types this match is flagged by, as <see cref="DataQualityIssueType"/> names.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// One entry per team. For lane queues each team is laid out across the five
    /// canonical positions (missing slots flagged); for laneless queues the slots
    /// are the raw participants in roster order.
    /// </summary>
    public IReadOnlyList<MatchTeamReadModel> Teams { get; init; } = [];
}

/// <summary>One team's roster, laid out by position with gaps highlighted.</summary>
public sealed record MatchTeamReadModel
{
    /// <summary>Riot team id (100/200), or a synthetic index for non-standard splits.</summary>
    public int TeamId { get; init; }

    /// <summary>Actual participant rows ingested for this team.</summary>
    public int PlayerCount { get; init; }

    /// <summary>
    /// Players a complete team should carry for this queue, or null when the
    /// queue has no profile (or the team id isn't a standard one). Lets the UI
    /// report "4/5 players" instead of inferring fullness from the slot list,
    /// which also contains unfilled lane gaps and appended unplaced members.
    /// </summary>
    public int? ExpectedPlayerCount { get; init; }

    /// <summary>
    /// Members whose <c>TeamPosition</c> didn't map onto a canonical lane
    /// (unknown/duplicate position) and were appended after the lane slots.
    /// They exist — the team isn't short — so the UI must report them as
    /// unplaced rather than implying a missing player. Always 0 for laneless
    /// queues.
    /// </summary>
    public int UnplacedCount { get; init; }

    /// <summary>Team result, or null when the team has no ingested rows.</summary>
    public bool? Win { get; init; }

    public IReadOnlyList<MatchSlotReadModel> Slots { get; init; } = [];
}

/// <summary>
/// One position slot on a team. For lane queues, <see cref="Position"/> is one of
/// the five canonical lanes and <see cref="Filled"/> is false when no participant
/// occupies it. For laneless queues, <see cref="Position"/> is empty and every
/// slot is filled (one per participant).
/// </summary>
public sealed record MatchSlotReadModel
{
    /// <summary>Canonical lane name for lane queues; empty for laneless queues.</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>False when this lane slot has no participant (a gap to highlight).</summary>
    public bool Filled { get; init; }

    public int? ParticipantId { get; init; }

    public int? ChampionId { get; init; }

    public string? SummonerName { get; init; }

    public bool? Win { get; init; }

    /// <summary>
    /// True when this filled slot shares its champion with another slot on the
    /// same team (the duplicate-champion signal), so the UI can mark both.
    /// </summary>
    public bool DuplicateChampion { get; init; }
}
