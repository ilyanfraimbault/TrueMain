namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// One match row in the truemain match history feed
/// (<c>GET /truemains/{nameTag}/matches</c>) — the data needed to render the
/// collapsed accordion header. Damage, vision score, performance score and
/// team objective counts are intentionally absent; they require ingestion
/// changes (see #159) and will be added once those land.
/// </summary>
public sealed record MatchSummaryReadModel
{
    public string MatchId { get; init; } = string.Empty;

    public int QueueId { get; init; }

    public string GameMode { get; init; } = string.Empty;

    public DateTime GameStartTimeUtc { get; init; }

    public int GameDurationSeconds { get; init; }

    public MatchSummarySelfReadModel Self { get; init; } = new();

    /// <summary>
    /// All 10 participants in row order (teamId 100 then teamId 200) so the
    /// frontend can render the versus thumbnails without re-sorting. Includes
    /// the self participant — the UI dedupes by puuid/championId as needed.
    /// </summary>
    public IReadOnlyList<MatchSummaryParticipantReadModel> Participants { get; init; }
        = Array.Empty<MatchSummaryParticipantReadModel>();
}

/// <summary>
/// The viewing player's slice of the match: championship pick, build,
/// summoner spells, keystone, KDA / CS, win/loss, and the MVP/ACE flag.
/// </summary>
public sealed record MatchSummarySelfReadModel
{
    public int ChampionId { get; init; }

    public int ChampionLevel { get; init; }

    public int Summoner1Id { get; init; }

    public int Summoner2Id { get; init; }

    /// <summary>Riot primary perk style (the keystone's tree).</summary>
    public int PrimaryStyleId { get; init; }

    /// <summary>Riot secondary perk style.</summary>
    public int SubStyleId { get; init; }

    /// <summary>
    /// The actual keystone perk id (slot 0 of the primary tree). Zero when
    /// the perk page failed to ingest cleanly — the frontend falls back to
    /// the tree icon.
    /// </summary>
    public int KeystoneId { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Assists { get; init; }

    /// <summary>Sum of lane minions + neutral monsters.</summary>
    public int Cs { get; init; }

    /// <summary>Kill participation 0..1 — <c>(kills + assists) / teamKills</c>, 0 when the team scored no kills.</summary>
    public double KillParticipation { get; init; }

    /// <summary>Inventory slots 0..5 (length 6) — the trinket is in <see cref="TrinketItemId"/>.</summary>
    public IReadOnlyList<int> Items { get; init; } = Array.Empty<int>();

    public int TrinketItemId { get; init; }

    /// <summary>100 = blue side, 200 = red side.</summary>
    public int TeamId { get; init; }

    /// <summary>
    /// The viewing player's Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/
    /// UTILITY), resolved from the PUUID-matched self participant. Null when
    /// Riot assigned none (non-SR modes, remakes). Exposed here so the frontend
    /// badges the role without re-identifying self by (team, champion) — which
    /// is ambiguous in queues that allow duplicate champions.
    /// </summary>
    public string? Position { get; init; }

    public bool Win { get; init; }

    /// <summary>
    /// LP gained or lost on this match. Null when the rank snapshots around
    /// the game's window are missing or span a tier/division transition.
    /// Always null in this iteration — derivation lands in a follow-up.
    /// </summary>
    public int? LpDelta { get; init; }

    /// <summary>True when this participant is the best on the winning side (KDA proxy).</summary>
    public bool IsMvp { get; init; }

    /// <summary>True when this participant is the best on the losing side (KDA proxy).</summary>
    public bool IsAce { get; init; }
}

public sealed record MatchSummaryParticipantReadModel
{
    public int ChampionId { get; init; }

    public int TeamId { get; init; }

    /// <summary>
    /// Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY). Null when Riot
    /// did not assign one (non-SR modes, remakes) — the frontend then falls
    /// back to raw participant order for the composition columns.
    /// </summary>
    public string? Position { get; init; }

    /// <summary>Riot game name (Riot ID prefix). Null when the participant is not a tracked account.</summary>
    public string? GameName { get; init; }

    /// <summary>Riot tag line (Riot ID suffix). Null when the participant is not a tracked account.</summary>
    public string? TagLine { get; init; }
}

/// <summary>
/// Paged response shape. <see cref="Total"/> is the count of matches
/// available for the player across all pages — the frontend uses it to
/// drive a classic page-number pagination control instead of a cursor.
/// </summary>
public sealed record MatchSummariesResponse
{
    public IReadOnlyList<MatchSummaryReadModel> Matches { get; init; }
        = Array.Empty<MatchSummaryReadModel>();

    /// <summary>1-indexed current page.</summary>
    public int Page { get; init; }

    /// <summary>Number of matches per page (the value the service clamped to).</summary>
    public int PageSize { get; init; }

    /// <summary>Total matches for the player across all pages.</summary>
    public int Total { get; init; }
}
