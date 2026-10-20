namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// The answer of <c>GET /truemains/{nameTag}/live</c> (#2103): whether the player is in a
/// game right now, and that game when they are. <see cref="CheckedAtUtc"/> is when Riot
/// was last asked, so a cached answer still says how fresh it is.
/// </summary>
public sealed record LiveGameReadModel
{
    /// <summary>One of <see cref="LiveGameStatuses"/>.</summary>
    public string Status { get; init; } = LiveGameStatuses.NotInGame;

    public DateTime CheckedAtUtc { get; init; }

    /// <summary>The game, only when <see cref="Status"/> is <see cref="LiveGameStatuses.InGame"/>.</summary>
    public LiveGameDetailReadModel? Game { get; init; }
}

/// <summary>The values <see cref="LiveGameReadModel.Status"/> takes.</summary>
public static class LiveGameStatuses
{
    public const string InGame = "inGame";

    public const string NotInGame = "notInGame";

    /// <summary>
    /// Riot could not be asked or did not answer (rate limited, down, no key configured):
    /// nothing is known about the player, which is not the same as "not in a game".
    /// </summary>
    public const string Unavailable = "unavailable";
}

public sealed record LiveGameDetailReadModel
{
    public long GameId { get; init; }

    public string PlatformId { get; init; } = string.Empty;

    public int QueueId { get; init; }

    public int MapId { get; init; }

    public string GameMode { get; init; } = string.Empty;

    /// <summary>Null while the players are still on the loading screen (Riot sends 0 then).</summary>
    public DateTime? StartedAtUtc { get; init; }

    /// <summary>Seconds of game time when Riot answered; 0 on the loading screen.</summary>
    public long GameLengthSeconds { get; init; }

    /// <summary>Blue side (100) first, then red (200).</summary>
    public IReadOnlyList<LiveTeamReadModel> Teams { get; init; } = [];
}

public sealed record LiveTeamReadModel
{
    public int TeamId { get; init; }

    /// <summary>Champions this team banned, in pick-turn order; "no ban" turns are left out.</summary>
    public IReadOnlyList<int> Bans { get; init; } = [];

    /// <summary>In lane order (top to support) when the lanes could be inferred, else Riot's order.</summary>
    public IReadOnlyList<LiveParticipantReadModel> Participants { get; init; } = [];
}

public sealed record LiveParticipantReadModel
{
    /// <summary>Null for a bot, or when Riot hides the player's Riot ID.</summary>
    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public int ChampionId { get; init; }

    public int ProfileIconId { get; init; }

    public int Spell1Id { get; init; }

    public int Spell2Id { get; init; }

    public int? KeystoneId { get; init; }

    public int? PrimaryStyleId { get; init; }

    public int? SecondaryStyleId { get; init; }

    /// <summary>
    /// The lane inferred from the champions and the Smite (spectator-v5 gives none): a Riot
    /// team position, or null outside a 5v5 Summoner's Rift game.
    /// </summary>
    public string? Position { get; init; }

    public bool IsBot { get; init; }

    /// <summary>The player whose profile asked for this game.</summary>
    public bool IsSubject { get; init; }

    /// <summary>TrueMain holds this account, so its profile can be linked.</summary>
    public bool IsTracked { get; init; }

    /// <summary>Solo/Duo rank; null when unranked or when it could not be read.</summary>
    public ProfileRankedReadModel? Ranked { get; init; }
}
