namespace Core.Lol.Map;

/// <summary>
/// Per-queue expectations used by the data-quality checks. Riot's match data is
/// shaped differently per queue, so a single hard-coded "10 players, 5 lanes"
/// rule would flood non-Summoner's-Rift queues with false positives. This
/// profile captures the few facts each check needs:
///   <list type="bullet">
///     <item>how many participant rows a complete match should carry;</item>
///     <item>whether lane/<c>TeamPosition</c> data is meaningful at all
///       (ARAM/Arena have none, so the missing-position and
///       duplicate-champion-per-team checks must not fire);</item>
///     <item>how many teams the queue splits into (2 for SR/ARAM, more for
///       Arena), so the per-team participant count is derived, not assumed.</item>
///   </list>
/// Only the queues the pipeline actually ingests are described; unknown queues
/// fall back to <see cref="Unknown"/> which disables every count/position rule
/// so an unrecognised queue can never be flagged on rules that don't apply to
/// it (the age-based missing-timeline check still applies — it is queue-agnostic).
/// </summary>
public sealed record QueueDataQualityProfile
{
    /// <summary>Total participant rows a complete match in this queue should have.</summary>
    public required int ExpectedParticipants { get; init; }

    /// <summary>
    /// Number of teams the queue splits players into (2 for SR/ARAM). Per-team
    /// participant count is <see cref="ExpectedParticipants"/> / this.
    /// </summary>
    public required int TeamCount { get; init; }

    /// <summary>
    /// True when <c>TeamPosition</c> carries a real lane assignment (only the
    /// 5v5 Summoner's Rift queues). When false the missing-position and
    /// duplicate-champion-per-team checks are skipped for this queue.
    /// </summary>
    public required bool HasLanes { get; init; }

    /// <summary>True when this queue is described (not the unknown fallback).</summary>
    public bool IsKnown { get; init; } = true;

    /// <summary>The five Summoner's Rift lane positions, in canonical order.</summary>
    public static readonly IReadOnlyList<string> LanePositions =
        ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    /// <summary>The two Summoner's Rift / ARAM team ids.</summary>
    public static readonly IReadOnlyList<int> StandardTeamIds = [100, 200];

    /// <summary>
    /// Fallback for a queue id we don't have a profile for: no participant-count
    /// or position expectations, so only the queue-agnostic checks apply.
    /// </summary>
    public static readonly QueueDataQualityProfile Unknown = new()
    {
        ExpectedParticipants = 0,
        TeamCount = 0,
        HasLanes = false,
        IsKnown = false
    };

    // 5v5 Summoner's Rift — 10 players across 2 teams, 5 lanes each. Shared by
    // every SR queue, so it's a single allocated instance rather than a property
    // that builds a fresh record on each read. Declared before Profiles so the
    // dictionary initialiser (which runs in textual order) sees the value.
    private static readonly QueueDataQualityProfile SummonersRift5v5 = new()
    {
        ExpectedParticipants = 10,
        TeamCount = 2,
        HasLanes = true
    };

    private static readonly IReadOnlyDictionary<int, QueueDataQualityProfile> Profiles =
        new Dictionary<int, QueueDataQualityProfile>
        {
            [(int)LolQueueId.RankedSoloDuo] = SummonersRift5v5,
            [(int)LolQueueId.Normal] = SummonersRift5v5,
            [(int)LolQueueId.RankedFlex] = SummonersRift5v5,
            [(int)LolQueueId.Clash] = SummonersRift5v5,
            // ARAM — 10 players across 2 teams, but NO lane assignment.
            [(int)LolQueueId.Aram] = new QueueDataQualityProfile
            {
                ExpectedParticipants = 10,
                TeamCount = 2,
                HasLanes = false
            }
        };

    /// <summary>
    /// Resolve the profile for a Riot queue id, or <see cref="Unknown"/> if the
    /// queue isn't one the pipeline describes.
    /// </summary>
    public static QueueDataQualityProfile ForQueue(int queueId)
        => Profiles.TryGetValue(queueId, out var profile) ? profile : Unknown;

    /// <summary>The queue ids that have a profile (i.e. support count/position rules).</summary>
    public static IReadOnlyCollection<int> KnownQueueIds => Profiles.Keys.ToArray();
}
