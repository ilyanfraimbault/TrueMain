using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public class RiotMatchDto
{
    [JsonPropertyName("metadata")]
    public RiotMatchMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("info")]
    public RiotMatchInfoDto Info { get; set; } = new();
}

public class RiotMatchMetadataDto
{
    [JsonPropertyName("matchId")]
    public string MatchId { get; set; } = string.Empty;
}

public class RiotMatchInfoDto
{
    [JsonPropertyName("queueId")]
    public int QueueId { get; set; }

    [JsonPropertyName("mapId")]
    public int MapId { get; set; }

    [JsonPropertyName("gameMode")]
    public string GameMode { get; set; } = string.Empty;

    [JsonPropertyName("gameType")]
    public string GameType { get; set; } = string.Empty;

    [JsonPropertyName("gameStartTimestamp")]
    public long GameStartTimestamp { get; set; }

    [JsonPropertyName("gameDuration")]
    public long GameDuration { get; set; }

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = string.Empty;

    [JsonPropertyName("participants")]
    public List<RiotParticipantDto> Participants { get; set; } = new();
}

public class RiotParticipantDto
{
    [JsonPropertyName("participantId")]
    public int ParticipantId { get; set; }

    [JsonPropertyName("puuid")]
    public string Puuid { get; set; } = string.Empty;

    [JsonPropertyName("summonerName")]
    public string SummonerName { get; set; } = string.Empty;

    [JsonPropertyName("summonerLevel")]
    public int SummonerLevel { get; set; }

    [JsonPropertyName("championId")]
    public int ChampionId { get; set; }

    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("teamPosition")]
    public string TeamPosition { get; set; } = string.Empty;

    [JsonPropertyName("individualPosition")]
    public string IndividualPosition { get; set; } = string.Empty;

    [JsonPropertyName("lane")]
    public string Lane { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("win")]
    public bool Win { get; set; }

    [JsonPropertyName("kills")]
    public int Kills { get; set; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; set; }

    [JsonPropertyName("assists")]
    public int Assists { get; set; }

    [JsonPropertyName("totalDamageDealtToChampions")]
    public int TotalDamageDealtToChampions { get; set; }

    [JsonPropertyName("visionScore")]
    public int VisionScore { get; set; }

    [JsonPropertyName("goldEarned")]
    public int GoldEarned { get; set; }

    [JsonPropertyName("totalMinionsKilled")]
    public int TotalMinionsKilled { get; set; }

    [JsonPropertyName("neutralMinionsKilled")]
    public int NeutralMinionsKilled { get; set; }

    [JsonPropertyName("champLevel")]
    public int ChampLevel { get; set; }

    [JsonPropertyName("item0")]
    public int Item0 { get; set; }

    [JsonPropertyName("item1")]
    public int Item1 { get; set; }

    [JsonPropertyName("item2")]
    public int Item2 { get; set; }

    [JsonPropertyName("item3")]
    public int Item3 { get; set; }

    [JsonPropertyName("item4")]
    public int Item4 { get; set; }

    [JsonPropertyName("item5")]
    public int Item5 { get; set; }

    [JsonPropertyName("item6")]
    public int Item6 { get; set; }

    [JsonPropertyName("summoner1Id")]
    public int Summoner1Id { get; set; }

    [JsonPropertyName("summoner2Id")]
    public int Summoner2Id { get; set; }

    [JsonPropertyName("perks")]
    public RiotPerksDto Perks { get; set; } = new();
}

public class RiotPerksDto
{
    [JsonPropertyName("statPerks")]
    public RiotStatPerksDto StatPerks { get; set; } = new();

    [JsonPropertyName("styles")]
    public List<RiotPerkStyleDto> Styles { get; set; } = new();
}

public class RiotStatPerksDto
{
    [JsonPropertyName("defense")]
    public int Defense { get; set; }

    [JsonPropertyName("flex")]
    public int Flex { get; set; }

    [JsonPropertyName("offense")]
    public int Offense { get; set; }
}

public class RiotPerkStyleDto
{
    [JsonPropertyName("style")]
    public int Style { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("selections")]
    public List<RiotPerkSelectionDto> Selections { get; set; } = new();
}

public class RiotPerkSelectionDto
{
    [JsonPropertyName("perk")]
    public int Perk { get; set; }
}
