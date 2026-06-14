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
    // Process-wide counter, never reset between tests: it only guarantees distinct
    // ParticipantIds within a match (the unique constraint), which holds as long as the
    // value keeps increasing. Integration tests share one serial [Collection], so there is
    // no parallel race. Callers must NOT assert on the absolute ParticipantId — pin an
    // explicit value via the parameter if a test needs a specific one.
    private static int _nextParticipantId;

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
        int? participantId = null)
    {
        // Default to a process-unique id so two participants seeded into the same match never
        // collide on the (MatchId, ParticipantId) unique index, even if a caller forgets to
        // pass distinct ids. Callers can still pin an explicit value when they assert on it.
        var resolvedParticipantId = participantId ?? Interlocked.Increment(ref _nextParticipantId);

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
            ParticipantId = resolvedParticipantId,
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
