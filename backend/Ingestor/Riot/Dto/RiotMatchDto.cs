using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotMatchDto
{
    [JsonPropertyName("metadata")]
    public RiotMatchMetadataDto Metadata { get; init; } = new();

    [JsonPropertyName("info")]
    public RiotMatchInfoDto Info { get; init; } = new();
}

public sealed record RiotMatchMetadataDto
{
    [JsonPropertyName("matchId")]
    public string MatchId { get; init; } = string.Empty;
}

public sealed record RiotMatchInfoDto
{
    [JsonPropertyName("queueId")]
    public int QueueId { get; init; }

    [JsonPropertyName("mapId")]
    public int MapId { get; init; }

    [JsonPropertyName("gameMode")]
    public string GameMode { get; init; } = string.Empty;

    [JsonPropertyName("gameType")]
    public string GameType { get; init; } = string.Empty;

    [JsonPropertyName("gameStartTimestamp")]
    public long GameStartTimestamp { get; init; }

    [JsonPropertyName("gameDuration")]
    public long GameDuration { get; init; }

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; init; } = string.Empty;

    [JsonPropertyName("gameCreation")]
    public long GameCreation { get; init; }

    [JsonPropertyName("gameEndTimestamp")]
    public long GameEndTimestamp { get; init; }

    /// <summary>
    /// How the game ended: <c>GameComplete</c> for a played game, <c>Abort_*</c> for a shell
    /// Riot recorded without one being played (#1364). Absent from old payloads, hence nullable.
    /// </summary>
    [JsonPropertyName("endOfGameResult")]
    public string? EndOfGameResult { get; init; }

    [JsonPropertyName("participants")]
    public IReadOnlyList<RiotParticipantDto> Participants { get; init; } = [];

    [JsonPropertyName("teams")]
    public IReadOnlyList<RiotTeamDto> Teams { get; init; } = [];
}

/// <summary>
/// The per-team block of a match-v5 payload. Only the champion-select bans are
/// bound (#920) — objectives and the team-level win flag are already derivable
/// from <see cref="RiotParticipantDto"/>, so binding them would duplicate state
/// the participant rows already carry.
/// </summary>
public sealed record RiotTeamDto
{
    [JsonPropertyName("teamId")]
    public int TeamId { get; init; }

    [JsonPropertyName("bans")]
    public IReadOnlyList<RiotBanDto> Bans { get; init; } = [];
}

public sealed record RiotBanDto
{
    /// <summary>
    /// The banned champion, or <c>-1</c> when that ban slot went unused (a player
    /// let the timer run out). Riot emits the slot either way, so the sentinel has
    /// to be filtered out before the ban is stored.
    /// </summary>
    [JsonPropertyName("championId")]
    public int ChampionId { get; init; }

    [JsonPropertyName("pickTurn")]
    public int PickTurn { get; init; }
}

public sealed record RiotParticipantDto
{
    [JsonPropertyName("participantId")]
    public int ParticipantId { get; init; }

    [JsonPropertyName("puuid")]
    public string Puuid { get; init; } = string.Empty;

    [JsonPropertyName("summonerName")]
    public string SummonerName { get; init; } = string.Empty;

    [JsonPropertyName("summonerLevel")]
    public int SummonerLevel { get; init; }

    [JsonPropertyName("championId")]
    public int ChampionId { get; init; }

    [JsonPropertyName("teamId")]
    public int TeamId { get; init; }

    [JsonPropertyName("teamPosition")]
    public string TeamPosition { get; init; } = string.Empty;

    [JsonPropertyName("individualPosition")]
    public string IndividualPosition { get; init; } = string.Empty;

    [JsonPropertyName("lane")]
    public string Lane { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("win")]
    public bool Win { get; init; }

    /// <summary>The remake vote passed. Riot repeats the same value on every participant.</summary>
    [JsonPropertyName("gameEndedInEarlySurrender")]
    public bool GameEndedInEarlySurrender { get; init; }

    [JsonPropertyName("kills")]
    public int Kills { get; init; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; init; }

    [JsonPropertyName("assists")]
    public int Assists { get; init; }

    [JsonPropertyName("totalDamageDealtToChampions")]
    public int TotalDamageDealtToChampions { get; init; }

    /// <summary>
    /// Per-participant context Riot reports alongside the totals (#1448): the
    /// damage split by type, healing and shielding, crowd control and damage
    /// taken. Nullable so a payload that omits one reads as "not measured"
    /// downstream instead of a zero.
    /// </summary>
    [JsonPropertyName("physicalDamageDealtToChampions")]
    public int? PhysicalDamageDealtToChampions { get; init; }

    [JsonPropertyName("magicDamageDealtToChampions")]
    public int? MagicDamageDealtToChampions { get; init; }

    [JsonPropertyName("trueDamageDealtToChampions")]
    public int? TrueDamageDealtToChampions { get; init; }

    [JsonPropertyName("totalHeal")]
    public int? TotalHeal { get; init; }

    [JsonPropertyName("totalHealsOnTeammates")]
    public int? TotalHealsOnTeammates { get; init; }

    [JsonPropertyName("totalDamageShieldedOnTeammates")]
    public int? TotalDamageShieldedOnTeammates { get; init; }

    [JsonPropertyName("timeCCingOthers")]
    public int? TimeCCingOthers { get; init; }

    [JsonPropertyName("totalTimeCCDealt")]
    public int? TotalTimeCCDealt { get; init; }

    [JsonPropertyName("totalDamageTaken")]
    public int? TotalDamageTaken { get; init; }

    [JsonPropertyName("damageSelfMitigated")]
    public int? DamageSelfMitigated { get; init; }

    [JsonPropertyName("visionScore")]
    public int VisionScore { get; init; }

    [JsonPropertyName("goldEarned")]
    public int GoldEarned { get; init; }

    [JsonPropertyName("totalMinionsKilled")]
    public int TotalMinionsKilled { get; init; }

    [JsonPropertyName("neutralMinionsKilled")]
    public int NeutralMinionsKilled { get; init; }

    [JsonPropertyName("champLevel")]
    public int ChampLevel { get; init; }

    [JsonPropertyName("item0")]
    public int Item0 { get; init; }

    [JsonPropertyName("item1")]
    public int Item1 { get; init; }

    [JsonPropertyName("item2")]
    public int Item2 { get; init; }

    [JsonPropertyName("item3")]
    public int Item3 { get; init; }

    [JsonPropertyName("item4")]
    public int Item4 { get; init; }

    [JsonPropertyName("item5")]
    public int Item5 { get; init; }

    [JsonPropertyName("item6")]
    public int Item6 { get; init; }

    [JsonPropertyName("roleBoundItem")]
    public int RoleBoundItem { get; init; }

    [JsonPropertyName("summoner1Id")]
    public int Summoner1Id { get; init; }

    [JsonPropertyName("summoner2Id")]
    public int Summoner2Id { get; init; }

    [JsonPropertyName("perks")]
    public RiotPerksDto Perks { get; init; } = new();
}

public sealed record RiotPerksDto
{
    [JsonPropertyName("statPerks")]
    public RiotStatPerksDto StatPerks { get; init; } = new();

    [JsonPropertyName("styles")]
    public IReadOnlyList<RiotPerkStyleDto> Styles { get; init; } = [];
}

public sealed record RiotStatPerksDto
{
    [JsonPropertyName("defense")]
    public int Defense { get; init; }

    [JsonPropertyName("flex")]
    public int Flex { get; init; }

    [JsonPropertyName("offense")]
    public int Offense { get; init; }
}

public sealed record RiotPerkStyleDto
{
    [JsonPropertyName("style")]
    public int Style { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("selections")]
    public IReadOnlyList<RiotPerkSelectionDto> Selections { get; init; } = [];
}

public sealed record RiotPerkSelectionDto
{
    [JsonPropertyName("perk")]
    public int Perk { get; init; }
}
