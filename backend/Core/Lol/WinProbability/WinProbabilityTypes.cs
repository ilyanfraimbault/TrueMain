namespace Core.Lol.WinProbability;

// The builder's input and output, mirroring web/shared/types/win-probability.ts
// (`WinProbabilityTimeline` in, `MatchWinProbability` out). Every probability and
// delta reads for team 100 (blue side).

/// <summary>
/// A game's timeline reduced to what the model reads. Names follow Riot's match-v5
/// timeline; the TS twin is <c>WinProbabilityTimeline</c>.
/// </summary>
public sealed record WinProbabilityTimeline(
    int DurationMs,
    IReadOnlyList<WinProbabilityTimelineParticipant> Participants,
    IReadOnlyList<WinProbabilityTimelineFrame> Frames,
    IReadOnlyList<WinProbabilityTimelineEvent> Events);

/// <summary>
/// A player of the game: <c>TeamId</c> 100 = blue side, 200 = red side; <c>Position</c> the Riot
/// team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY), empty when unknown.
/// </summary>
public sealed record WinProbabilityTimelineParticipant(int ParticipantId, int TeamId, string Position);

/// <summary>One frame of the timeline (one a minute).</summary>
public sealed record WinProbabilityTimelineFrame(int Ms, IReadOnlyList<WinProbabilityFramePlayer> Players);

/// <summary>A player's state in a frame; <c>Cs</c> is lane minions plus jungle monsters.</summary>
public sealed record WinProbabilityFramePlayer(int ParticipantId, int Cs, int Level);

/// <summary>A timeline event; only <c>CHAMPION_KILL</c>, <c>BUILDING_KILL</c> and <c>ELITE_MONSTER_KILL</c> are read.</summary>
public sealed record WinProbabilityTimelineEvent
{
    public string Type { get; init; } = string.Empty;

    public int Ms { get; init; }

    public int? KillerId { get; init; }

    public int? VictimId { get; init; }

    public IReadOnlyList<int>? AssistIds { get; init; }

    /// <summary>Epic monsters: the side that took it, when the timeline names it.</summary>
    public int? KillerTeamId { get; init; }

    /// <summary>Buildings: the side that <em>lost</em> it.</summary>
    public int? TeamId { get; init; }

    public string? BuildingType { get; init; }

    public string? LaneType { get; init; }

    public string? TowerType { get; init; }

    public string? MonsterType { get; init; }

    public string? MonsterSubType { get; init; }

    /// <summary>Kills: bounty plus shutdown bounty, when the timeline carries them.</summary>
    public int? Bounty { get; init; }
}

/// <summary>The curve and the moments that swung it, always read for team 100 (TS <c>MatchWinProbability</c>).</summary>
public sealed record WinProbabilityCurve(
    IReadOnlyList<WinProbabilityPoint> Points,
    IReadOnlyList<WinProbabilitySwing> Swings,
    IReadOnlyList<WinProbabilityObjective> Objectives);

/// <summary>Team 100's chance to win (<c>P</c>, 0..1) at <c>Ms</c> milliseconds of game time.</summary>
public sealed record WinProbabilityPoint(int Ms, double P);

/// <summary>An event that moved the chance (TS <c>WinProbabilitySwing</c>).</summary>
/// <param name="Ms">Game time of the event, milliseconds.</param>
/// <param name="Kind">One of <see cref="WinProbabilitySwingKinds"/>.</param>
/// <param name="TeamId">The side the event is for: the killer's, or the side that took the building or monster.</param>
/// <param name="Delta">Team 100's chance after the event minus before it, at the event's time.</param>
/// <param name="KillerId">Kills: the killer's participant id; 0 otherwise.</param>
/// <param name="VictimId">Kills: the victim's participant id; 0 otherwise.</param>
/// <param name="Assists">Kills: how many assisted; 0 otherwise.</param>
/// <param name="Bounty">Kills: bounty plus shutdown, null when the timeline does not carry it.</param>
/// <param name="Lane">Buildings: <c>TOP_LANE</c> / <c>MID_LANE</c> / <c>BOT_LANE</c>; null otherwise.</param>
/// <param name="TowerType">Turrets: <c>OUTER_TURRET</c> … <c>NEXUS_TURRET</c>; null otherwise.</param>
/// <param name="MonsterSubType">Drakes: the kind (<c>FIRE_DRAGON</c>, …); null otherwise.</param>
public sealed record WinProbabilitySwing(
    int Ms,
    string Kind,
    int TeamId,
    double Delta,
    int KillerId,
    int VictimId,
    int Assists,
    int? Bounty,
    string? Lane,
    string? TowerType,
    string? MonsterSubType);

/// <summary>An epic monster taken (TS <c>WinProbabilityObjective</c>).</summary>
/// <param name="Ms">Game time of the kill, milliseconds.</param>
/// <param name="MonsterType"><c>DRAGON</c>, <c>BARON_NASHOR</c>, <c>RIFTHERALD</c>, <c>HORDE</c> (Voidgrubs), <c>ATAKHAN</c>.</param>
/// <param name="MonsterSubType">Drakes: the kind; null otherwise.</param>
/// <param name="TeamId">The side that took it.</param>
/// <param name="Delta">As a swing's; null for a monster the model does not weigh — never shown as zero.</param>
public sealed record WinProbabilityObjective(
    int Ms,
    string MonsterType,
    string? MonsterSubType,
    int TeamId,
    double? Delta);

/// <summary>The turning-point kinds, as the TS <c>WinProbabilitySwingKind</c> spells them.</summary>
public static class WinProbabilitySwingKinds
{
    public const string Kill = "kill";
    public const string Turret = "turret";
    public const string Inhibitor = "inhibitor";
    public const string Dragon = "dragon";
    public const string Elder = "elder";
    public const string Baron = "baron";
}
