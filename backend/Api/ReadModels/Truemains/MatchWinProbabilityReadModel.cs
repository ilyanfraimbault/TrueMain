namespace TrueMain.ReadModels.Truemains;

// The post-game win-probability curve and its turning points (#1911), computed at
// ingest (Core/Lol/WinProbability) and stored in match_win_probability. Mirrors
// web/shared/types/win-probability.ts (`MatchWinProbability`) field for field;
// every probability and delta reads for team 100 (blue side).

/// <summary>The curve and the moments that swung it, always read for team 100.</summary>
public sealed record MatchWinProbabilityReadModel
{
    /// <summary>Team 100's chance to win at each timeline frame (one a minute) and at the end.</summary>
    public IReadOnlyList<WinProbabilityPointReadModel> Points { get; init; } = [];

    /// <summary>The events that moved the chance most, largest |delta| first.</summary>
    public IReadOnlyList<WinProbabilitySwingReadModel> Swings { get; init; } = [];

    /// <summary>Every epic monster taken, those the model does not weigh included (<c>delta</c> null).</summary>
    public IReadOnlyList<WinProbabilityObjectiveReadModel> Objectives { get; init; } = [];
}

public sealed record WinProbabilityPointReadModel
{
    /// <summary>Game time, milliseconds.</summary>
    public int Ms { get; init; }

    /// <summary>Team 100's chance to win, 0..1.</summary>
    public double P { get; init; }
}

public sealed record WinProbabilitySwingReadModel
{
    public int Ms { get; init; }

    /// <summary><c>kill</c> / <c>turret</c> / <c>inhibitor</c> / <c>dragon</c> / <c>elder</c> / <c>baron</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The side the event is for: the killer's, or the side that took the building or monster.</summary>
    public int TeamId { get; init; }

    /// <summary>Team 100's chance after the event minus before it, at the event's time.</summary>
    public double Delta { get; init; }

    /// <summary>Kills: the killer's participant id; 0 otherwise.</summary>
    public int KillerId { get; init; }

    /// <summary>Kills: the victim's participant id; 0 otherwise.</summary>
    public int VictimId { get; init; }

    /// <summary>Kills: how many assisted.</summary>
    public int Assists { get; init; }

    /// <summary>Kills: bounty plus shutdown, null when the timeline does not carry it.</summary>
    public int? Bounty { get; init; }

    /// <summary>Buildings: <c>TOP_LANE</c> / <c>MID_LANE</c> / <c>BOT_LANE</c>; null otherwise.</summary>
    public string? Lane { get; init; }

    /// <summary>Turrets: <c>OUTER_TURRET</c> … <c>NEXUS_TURRET</c>; null otherwise.</summary>
    public string? TowerType { get; init; }

    /// <summary>Drakes: the kind (<c>FIRE_DRAGON</c>, …); null otherwise.</summary>
    public string? MonsterSubType { get; init; }
}

public sealed record WinProbabilityObjectiveReadModel
{
    public int Ms { get; init; }

    /// <summary><c>DRAGON</c>, <c>BARON_NASHOR</c>, <c>RIFTHERALD</c>, <c>HORDE</c>, <c>ATAKHAN</c>.</summary>
    public string MonsterType { get; init; } = string.Empty;

    public string? MonsterSubType { get; init; }

    public int TeamId { get; init; }

    /// <summary>As a swing's; null for a monster the model does not weigh.</summary>
    public double? Delta { get; init; }
}
