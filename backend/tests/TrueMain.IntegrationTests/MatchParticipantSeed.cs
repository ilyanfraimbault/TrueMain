using Data;
using Data.Entities;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Shared seeding for harvest tests: adds a match plus one participant row, so the two
/// harvest suites don't drift apart if the schema evolves. Pass a non-null
/// <c>riotAccountId</c> to simulate a tracked (non-orphan) participant.
/// </summary>
internal static class MatchParticipantSeed
{
    public static void AddMatchWithParticipant(
        TrueMainDbContext db,
        string matchId,
        string platformId,
        int queueId,
        DateTime gameStartTimeUtc,
        string puuid,
        int championId,
        bool win,
        Guid? riotAccountId = null,
        int participantId = 1)
    {
        db.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = platformId,
            QueueId = queueId,
            MapId = queueId == 450 ? 12 : 11,
            GameMode = queueId == 450 ? "ARAM" : "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = 1800,
            GameVersion = "16.6.1",
            CreatedAtUtc = gameStartTimeUtc,
            TimelineIngested = true
        });

        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            RiotAccountId = riotAccountId,
            Puuid = puuid,
            SummonerName = puuid,
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "CARRY",
            Win = win,
            Kills = 1,
            Deaths = 1,
            Assists = 1,
            GoldEarned = 10000,
            TotalMinionsKilled = 100,
            NeutralMinionsKilled = 0,
            ChampLevel = 14,
            Item0 = 6672,
            Item1 = 3006,
            Item6 = 3363,
            TrinketItemId = 3363,
            PrimaryStyleId = 8000,
            SubStyleId = 8200,
            Summoner1Id = 4,
            Summoner2Id = 7,
            ItemEvents = [],
            SkillEvents = []
        });
    }
}
