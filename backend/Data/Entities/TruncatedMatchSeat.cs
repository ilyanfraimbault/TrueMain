namespace Data.Entities;

/// <summary>
/// One seat of a ranked match that match retention deleted, kept truncated (#2368, #2358):
/// the build facts the matchup and draft reads fold, and the end-of-game line a profile row
/// draws — no timeline, no participant JSON. Written by match retention in the same
/// transaction that deletes the match, and purged on its own two-tier window: every seat
/// for <c>MatchDataRetention:TruncatedRetainedPatchCount</c> patches, then the tracked
/// accounts' seats to the end of the season.
/// </summary>
/// <remarks>
/// The build facts are resolved at truncation with the primitives every live build read
/// uses (<c>StarterItemAnalyzer</c>, <c>FinalBuildResolver</c>, <c>BootsResolver</c>,
/// <c>SkillOrderBuilder</c>), so a truncated game reads as the same build it was while live.
/// </remarks>
public class TruncatedMatchSeat
{
    public string MatchId { get; set; } = string.Empty;

    /// <summary>Riot's 1-10 slot, unique within the match.</summary>
    public int ParticipantId { get; set; }

    public string PlatformId { get; set; } = string.Empty;
    public int QueueId { get; set; }

    /// <summary>The match's <c>major.minor</c> patch.</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>The full game version, which item data is keyed by.</summary>
    public string GameVersion { get; set; } = string.Empty;

    public DateTime GameStartTimeUtc { get; set; }
    public int GameDurationSeconds { get; set; }

    /// <summary>The seat's rank band, as <c>match_participants</c> carried it.</summary>
    public string EloBracket { get; set; } = string.Empty;

    public int TeamId { get; set; }
    public bool Win { get; set; }
    public int ChampionId { get; set; }
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>The account key: the tracked account when the seat was one, else null.</summary>
    public Guid? RiotAccountId { get; set; }

    public string Puuid { get; set; } = string.Empty;

    public int Summoner1Id { get; set; }
    public int Summoner2Id { get; set; }

    public int PrimaryStyleId { get; set; }
    public int SubStyleId { get; set; }

    /// <summary>The six chosen perks, primary tree first (keystone first), then the secondary; empty when unknown.</summary>
    public int[] RunePerkIds { get; set; } = [];

    public int PerksOffense { get; set; }
    public int PerksFlex { get; set; }
    public int PerksDefense { get; set; }

    public int[] StarterItemIds { get; set; } = [];

    /// <summary>Completed build items in completion order, boots excluded; empty when item data was missing.</summary>
    public int[] BuildItemIds { get; set; } = [];

    /// <summary>The minute each <see cref="BuildItemIds"/> entry was completed, index for index.</summary>
    public int[] BuildItemMinutes { get; set; } = [];

    /// <summary>Boots item id, 0 when none was resolved.</summary>
    public int BootsItemId { get; set; }

    /// <summary>Max-order key (<c>"Q-W-E"</c>), empty when unknown.</summary>
    public string SkillOrderKey { get; set; } = string.Empty;

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public DateTime TruncatedAtUtc { get; set; }
}
