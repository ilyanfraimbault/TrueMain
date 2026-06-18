using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MatchParticipantKillPositionIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MatchParticipantKillPositionIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Repository_RoundTrips_AndDeletesByMatch()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Matches.Add(new MatchBuilder().WithId("m-kp-1").Build());
            seed.MatchParticipantKillPositions.AddRange(
                Position("m-kp-1", 2, 120_000, 1000, 2000),
                Position("m-kp-1", 2, 300_000, 4000, 5000),
                Position("m-kp-1", 3, 200_000, 6000, 7000));
            await seed.SaveChangesAsync();
        }

        await using var db = _fixture.CreateDbContext();
        var repository = new MatchParticipantKillPositionRepository(db);

        var rows = await repository.GetByMatchIdAsync("m-kp-1", CancellationToken.None);
        rows.Should().HaveCount(3);
        // Ordered by participant, then timestamp.
        rows.Select(r => r.ParticipantId).Should().Equal(2, 2, 3);
        rows[0].TimestampMs.Should().Be(120_000);
        rows[0].X.Should().Be(1000);

        var deleted = await repository.DeleteByMatchIdAsync("m-kp-1", CancellationToken.None);
        deleted.Should().Be(3);
        (await repository.GetByMatchIdAsync("m-kp-1", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Positions_CascadeDelete_WithMatch()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Matches.Add(new MatchBuilder().WithId("m-kp-2").Build());
            seed.MatchParticipantKillPositions.Add(Position("m-kp-2", 1, 100_000, 500, 500));
            await seed.SaveChangesAsync();
        }

        await using (var del = _fixture.CreateDbContext())
        {
            var match = await del.Matches.FindAsync("m-kp-2");
            del.Matches.Remove(match!);
            await del.SaveChangesAsync();
        }

        await using var db = _fixture.CreateDbContext();
        var repository = new MatchParticipantKillPositionRepository(db);
        (await repository.GetByMatchIdAsync("m-kp-2", CancellationToken.None))
            .Should().BeEmpty("the FK cascade removes positions with their match");
    }

    private static MatchParticipantKillPosition Position(string matchId, int participantId, int timestampMs, int x, int y)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            TimestampMs = timestampMs,
            X = x,
            Y = y
        };
}
