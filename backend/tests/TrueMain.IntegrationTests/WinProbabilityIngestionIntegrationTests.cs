using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The timeline pass writes the match's win-probability row (#1911): one row per match,
/// replaced (not duplicated) when the timeline is ingested again, none for a game the
/// builder gives no curve.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class WinProbabilityIngestionIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly string[] Positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TimelineIngestion_WritesTheCurve_AndReplacesItOnReingest()
    {
        const string matchId = "KR_WINPROB_1";
        await SeedMatchAsync(matchId, gameDurationSeconds: 1_800);

        await IngestAsync(matchId);
        await IngestAsync(matchId);

        await using var db = fixture.CreateDbContext();
        var row = await db.MatchWinProbabilities.AsNoTracking().SingleAsync(w => w.MatchId == matchId);
        // Frames at every minute 0..30 — the last one is the end of the game itself.
        row.Points.Should().HaveCount(31);
        row.Points[^1].Ms.Should().Be(1_800_000);
        row.Points[^1].P.Should().BeGreaterThan(0.5, "blue out-farms red in every lane and took the first blood");
        var kill = row.Swings.Should().ContainSingle().Subject;
        kill.Kind.Should().Be("kill");
        kill.TeamId.Should().Be(100);
        kill.KillerId.Should().Be(1);
        kill.VictimId.Should().Be(6);
        kill.Assists.Should().Be(1);
        kill.Bounty.Should().Be(450, "bounty plus shutdown bounty");
        kill.Delta.Should().BePositive();
        var herald = row.Objectives.Should().ContainSingle().Subject;
        herald.MonsterType.Should().Be("RIFTHERALD");
        herald.TeamId.Should().Be(200);
        herald.Delta.Should().BeNull();
    }

    [Fact]
    public async Task TimelineIngestion_WritesNoCurve_UnderFifteenMinutes()
    {
        const string matchId = "KR_WINPROB_SHORT";
        await SeedMatchAsync(matchId, gameDurationSeconds: 840);

        await IngestAsync(matchId);

        await using var db = fixture.CreateDbContext();
        (await db.MatchWinProbabilities.AnyAsync(w => w.MatchId == matchId)).Should().BeFalse();
        (await db.Matches.SingleAsync(m => m.Id == matchId)).TimelineIngested.Should().BeTrue();
    }

    private async Task IngestAsync(string matchId)
    {
        await using var db = fixture.CreateDbContext();
        await using var session = new DataSession(db);
        var service = new TimelineIngestionService(new FakeRiotMatchClient(), NullLogger<TimelineIngestionService>.Instance);
        var plan = new TimelineIngestionPlan(
            [new FetchedTimeline(matchId, await new FakeRiotMatchClient().GetTimelineAsync(matchId, RegionalRoute.Asia, CancellationToken.None))]);

        var updated = await service.WriteAsync(session, plan, saveBatchSize: 10, CancellationToken.None);

        updated.Should().Be(1);
    }

    private async Task SeedMatchAsync(string matchId, int gameDurationSeconds)
    {
        await using var db = fixture.CreateDbContext();
        db.Matches.Add(new MatchBuilder().WithId(matchId).WithGameDurationSeconds(gameDurationSeconds).Build());
        for (var participantId = 1; participantId <= 10; participantId++)
        {
            db.MatchParticipants.Add(new MatchParticipantBuilder()
                .WithMatchId(matchId)
                .WithParticipantId(participantId)
                .WithPuuid($"{matchId}-puuid-{participantId}")
                .WithTeamId(participantId <= 5 ? 100 : 200)
                .WithTeamPosition(Positions[(participantId - 1) % 5])
                .Build());
        }

        await db.SaveChangesAsync();
    }

    private sealed class FakeRiotMatchClient : IRiotMatchClient
    {
        public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<string>> GetMatchIdsAsync(MatchIdQuery query, CancellationToken ct)
            => throw new NotSupportedException();

        /// <summary>A frame a minute up to 30:00, blue a creep ahead per minute in every lane.</summary>
        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => Task.FromResult(new MatchTimelineDto
            {
                Frames = Enumerable.Range(0, 31)
                    .Select(minute => new MatchTimelineFrameDto
                    {
                        TimestampMs = minute * 60_000,
                        ParticipantFrames = Enumerable.Range(1, 10)
                            .Select(participantId => new MatchParticipantFrameDto
                            {
                                ParticipantId = participantId,
                                MinionsKilled = minute * (participantId <= 5 ? 8 : 7),
                                Level = 1 + (minute / 2),
                            })
                            .ToList(),
                    })
                    .ToList(),
                Events =
                [
                    new MatchTimelineEventDto
                    {
                        Type = "CHAMPION_KILL", TimestampMs = 200_000, KillerId = 1, VictimId = 6,
                        AssistingParticipantIds = [2], Bounty = 300, ShutdownBounty = 150,
                    },
                    new MatchTimelineEventDto
                    {
                        Type = "ELITE_MONSTER_KILL", TimestampMs = 840_000, KillerId = 7, KillerTeamId = 200,
                        MonsterType = "RIFTHERALD",
                    },
                ],
            });
    }
}
