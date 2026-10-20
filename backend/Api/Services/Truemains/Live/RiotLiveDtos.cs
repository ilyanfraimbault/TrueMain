namespace TrueMain.Services.Truemains.Live;

/// <summary>
/// spectator-v5's <c>CurrentGameInfo</c>, reduced to what the live tab draws. Read with the
/// web (camelCase) defaults of <c>ReadFromJsonAsync</c>.
/// </summary>
public sealed record RiotActiveGameDto
{
    public long GameId { get; init; }

    public string? GameMode { get; init; }

    public int MapId { get; init; }

    public int GameQueueConfigId { get; init; }

    public string? PlatformId { get; init; }

    /// <summary>Epoch milliseconds; 0 while the players are on the loading screen.</summary>
    public long GameStartTime { get; init; }

    /// <summary>Seconds of game time when Riot answered.</summary>
    public long GameLength { get; init; }

    public List<RiotLiveBanDto> BannedChampions { get; init; } = [];

    public List<RiotLiveParticipantDto> Participants { get; init; } = [];
}

public sealed record RiotLiveBanDto
{
    public int ChampionId { get; init; }

    public int TeamId { get; init; }

    public int PickTurn { get; init; }
}

public sealed record RiotLiveParticipantDto
{
    /// <summary>Null for a bot.</summary>
    public string? Puuid { get; init; }

    /// <summary><c>Name#TAG</c>; may be missing for a bot.</summary>
    public string? RiotId { get; init; }

    public int TeamId { get; init; }

    public int ChampionId { get; init; }

    public int ProfileIconId { get; init; }

    public long Spell1Id { get; init; }

    public long Spell2Id { get; init; }

    public bool Bot { get; init; }

    public RiotLivePerksDto? Perks { get; init; }
}

public sealed record RiotLivePerksDto
{
    public List<long> PerkIds { get; init; } = [];

    public long PerkStyle { get; init; }

    public long PerkSubStyle { get; init; }
}

/// <summary>league-v4's <c>LeagueEntryDTO</c>, the fields the rank badge needs.</summary>
public sealed record RiotLiveLeagueEntryDto
{
    public string? QueueType { get; init; }

    public string? Tier { get; init; }

    public string? Rank { get; init; }

    public int LeaguePoints { get; init; }

    public int Wins { get; init; }

    public int Losses { get; init; }
}
