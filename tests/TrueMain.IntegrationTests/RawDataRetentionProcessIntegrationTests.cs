using Data;
using Data.Entities;
using FluentAssertions;
using Core.Options;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace TrueMain.IntegrationTests;

public sealed class RawDataRetentionProcessIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RawDataRetentionProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldDeleteRawMatchesOutsideRetainedPatchWindow()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedRetentionDataAsync();

        var process = new RawDataRetentionProcess(
            NullLogger<RawDataRetentionProcess>.Instance,
            new TestDbContextFactory(_fixture),
            new ProcessRunRecorder(_fixture.CreateSessionFactory()),
            Options.Create(new RawDataRetentionOptions
            {
                RetainedPatchCount = 1
            }),
            Options.Create(new MainAnalysisOptions
            {
                QueueId = 420
            }));

        await process.RunAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var remainingMatchIds = await db.Matches
            .AsNoTracking()
            .OrderBy(match => match.Id)
            .Select(match => match.Id)
            .ToListAsync();

        remainingMatchIds.Should().BeEquivalentTo(["RET_2", "RET_4", "RET_5"]);
        (await db.MatchParticipants.AsNoTracking().CountAsync()).Should().Be(3);
        (await db.ProcessRuns.AsNoTracking().AnyAsync(run => run.ProcessName == "RawDataRetention")).Should().BeTrue();
    }

    private async Task SeedRetentionDataAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.Matches.AddRange(
            BuildMatch("RET_1", "KR", now.AddDays(-10), "16.3.7"),
            BuildMatch("RET_2", "KR", now.AddDays(-1), "16.4.2"),
            BuildMatch("RET_3", "NA1", now.AddDays(-2), "16.4.1"),
            BuildMatch("RET_4", "NA1", now, "16.5.1"),
            BuildMatch("RET_5", "KR", now.AddHours(-1), "16.6.1", queueId: 450, gameMode: "ARAM", mapId: 12));

        db.MatchParticipants.AddRange(
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888881"), "RET_1"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888882"), "RET_2"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888883"), "RET_3"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888884"), "RET_4"),
            BuildParticipant(Guid.Parse("88888888-8888-8888-8888-888888888885"), "RET_5"));

        await db.SaveChangesAsync();
    }

    private static Match BuildMatch(
        string id,
        string platformId,
        DateTime gameStartTimeUtc,
        string gameVersion,
        int queueId = 420,
        string gameMode = "CLASSIC",
        int mapId = 11)
    {
        return new Match
        {
            Id = id,
            PlatformId = platformId,
            QueueId = queueId,
            MapId = mapId,
            GameMode = gameMode,
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = 1800,
            GameVersion = gameVersion,
            CreatedAtUtc = gameStartTimeUtc,
            TimelineIngested = true
        };
    }

    private static MatchParticipant BuildParticipant(Guid id, string matchId)
    {
        return new MatchParticipant
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = $"{matchId}-puuid",
            SummonerName = $"{matchId}-puuid",
            SummonerLevel = 100,
            ChampionId = 22,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "CARRY",
            Win = true,
            Kills = 1,
            Deaths = 1,
            Assists = 1,
            GoldEarned = 10000,
            TotalMinionsKilled = 100,
            NeutralMinionsKilled = 0,
            ChampLevel = 14,
            Item0 = 6672,
            Item1 = 3006,
            Item2 = 0,
            Item3 = 0,
            Item4 = 0,
            Item5 = 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5002,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8200,
            Summoner1Id = 4,
            Summoner2Id = 7,
            ItemEvents = [],
            SkillEvents = []
        };
    }

    private sealed class TestDbContextFactory(PostgresFixture fixture) : IDbContextFactory<TrueMainDbContext>
    {
        [SuppressMessage("Reliability", "CA2000", Justification = "DbContext ownership is transferred to the caller.")]
        public ValueTask<TrueMainDbContext> CreateDbContextAsync(CancellationToken _ = default)
            => ValueTask.FromResult(fixture.CreateDbContext());

        [SuppressMessage("Reliability", "CA2000", Justification = "DbContext ownership is transferred to the caller.")]
        public TrueMainDbContext CreateDbContext()
            => fixture.CreateDbContext();
    }
}
