using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// How a match ended, as #1364 reads it from the payload: the three timestamps and
/// <c>endOfGameResult</c> carried onto <see cref="Match"/>, an <c>Abort_*</c> shell told
/// apart from a game, and Riot's remake vote lifted from the participants to the match.
/// Driven through the real deserializer, so a mistyped <c>[JsonPropertyName]</c> fails here.
/// </summary>
public sealed class RiotMatchMapperEndOfGameTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ACompletedGameCarriesItsEndOfGameFieldsOntoTheMatch()
    {
        var dto = Deserialize(Payload(endOfGameResult: "\"GameComplete\"", earlySurrender: false));

        var match = Map(dto);

        RiotMatchMapper.IsCompletedGame(dto).Should().BeTrue();
        match.EndOfGameResult.Should().Be("GameComplete");
        match.GameCreationUtc.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1751200000000).UtcDateTime);
        match.GameEndTimestampUtc.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1751201860000).UtcDateTime);
        match.EndedInEarlySurrender.Should().BeFalse();
    }

    [Theory]
    [InlineData("Abort_Unexpected")]
    [InlineData("Abort_TooFewPlayers")]
    [InlineData("Abort_AntiCheat")]
    public void AnAbortedMatchIsAShellNotAGame(string result)
        => RiotMatchMapper.IsCompletedGame(Deserialize(Payload(endOfGameResult: $"\"{result}\"", earlySurrender: false)))
            .Should().BeFalse();

    [Fact]
    public void APayloadWithoutTheFieldPredatesItAndIsTakenAsAGame()
    {
        var dto = Deserialize(Payload(endOfGameResult: null, earlySurrender: false));

        RiotMatchMapper.IsCompletedGame(dto).Should().BeTrue();
        Map(dto).EndOfGameResult.Should().BeNull();
    }

    [Fact]
    public void RiotRemakeVoteIsLiftedOntoTheMatch()
        => Map(Deserialize(Payload(endOfGameResult: "\"GameComplete\"", earlySurrender: true)))
            .EndedInEarlySurrender.Should().BeTrue();

    [Fact]
    public void MissingTimestampsReadAsUnknownNotAsTheEpoch()
    {
        var dto = new RiotMatchDto
        {
            Metadata = new RiotMatchMetadataDto { MatchId = "KR_1" },
            Info = new RiotMatchInfoDto { QueueId = 420, GameDuration = 1800 }
        };

        var match = Map(dto);

        match.GameCreationUtc.Should().BeNull();
        match.GameEndTimestampUtc.Should().BeNull();
        match.EndedInEarlySurrender.Should().BeFalse();
    }

    private static Match Map(RiotMatchDto dto)
        => RiotMatchMapper.Map(dto, "KR", new Dictionary<AccountKey, RiotAccount>(), FixedNow).Match;

    private static RiotMatchDto Deserialize(string json)
        => JsonSerializer.Deserialize<RiotMatchDto>(json, RiotJson.Options)!;

    /// <summary>A match-v5 body trimmed to the fields under test, with Riot's casing.</summary>
    private static string Payload(string? endOfGameResult, bool earlySurrender)
    {
        var endOfGame = endOfGameResult is null ? string.Empty : $"\"endOfGameResult\": {endOfGameResult},";
        var surrender = earlySurrender ? "true" : "false";
        return $$"""
        {
          "metadata": { "matchId": "KR_7412001234" },
          "info": {
            {{endOfGame}}
            "gameCreation": 1751200000000,
            "gameStartTimestamp": 1751200060000,
            "gameEndTimestamp": 1751201860000,
            "gameDuration": 1800,
            "gameVersion": "16.13.703.1234",
            "queueId": 420,
            "participants": [
              { "participantId": 1, "puuid": "a", "teamId": 100, "gameEndedInEarlySurrender": {{surrender}}, "perks": { "statPerks": { "defense": 5001, "flex": 5008, "offense": 5005 }, "styles": [] } },
              { "participantId": 6, "puuid": "b", "teamId": 200, "gameEndedInEarlySurrender": {{surrender}}, "perks": { "statPerks": { "defense": 5001, "flex": 5008, "offense": 5005 }, "styles": [] } }
            ],
            "teams": []
          }
        }
        """;
    }
}
