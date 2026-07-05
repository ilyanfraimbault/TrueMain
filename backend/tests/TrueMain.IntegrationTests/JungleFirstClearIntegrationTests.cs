using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class JungleFirstClearIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public JungleFirstClearIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Repository_RoundTripsStepsJsonb_AndDeletesByMatch()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Matches.Add(new MatchBuilder().WithId("m-jfc-1").Build());
            seed.JungleFirstClears.AddRange(
                Clear("m-jfc-1", 6, fullClearMs: null, ("RedGromp", 60_000), ("RedBlueBuff", 120_000)),
                Clear("m-jfc-1", 1, fullClearMs: 180_000,
                    ("BlueGromp", 60_000), ("BlueBlueBuff", 120_000), ("BlueWolves", 180_000)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fixture.CreateDbContext();
        var repository = new JungleFirstClearRepository(db);

        var rows = await repository.GetByMatchIdAsync("m-jfc-1", CancellationToken.None);
        rows.Should().HaveCount(2);
        // Ordered by participant.
        rows.Select(r => r.ParticipantId).Should().Equal(1, 6);

        var jungler = rows[0];
        jungler.FullClearTimeMs.Should().Be(180_000);
        // The JSONB Steps survive the round-trip, in order.
        jungler.Steps.Select(s => s.Camp).Should().Equal("BlueGromp", "BlueBlueBuff", "BlueWolves");
        jungler.Steps.Select(s => s.TimestampMs).Should().Equal(60_000, 120_000, 180_000);
        rows[1].FullClearTimeMs.Should().BeNull();

        var deleted = await repository.DeleteByMatchIdAsync("m-jfc-1", CancellationToken.None);
        deleted.Should().Be(2);
        (await repository.GetByMatchIdAsync("m-jfc-1", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateJunglerForMatch_ViolatesUniqueIndex()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        db.Matches.Add(new MatchBuilder().WithId("m-jfc-2").Build());
        db.JungleFirstClears.AddRange(
            Clear("m-jfc-2", 1, fullClearMs: null, ("BlueGromp", 60_000)),
            Clear("m-jfc-2", 1, fullClearMs: null, ("BlueBlueBuff", 120_000)));

        var save = async () => await db.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>(
            "the unique (MatchId, ParticipantId) index allows one first clear per jungler per match");
    }

    [Fact]
    public async Task FirstClears_CascadeDelete_WithMatch()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Matches.Add(new MatchBuilder().WithId("m-jfc-3").Build());
            seed.JungleFirstClears.Add(Clear("m-jfc-3", 1, fullClearMs: null, ("BlueGromp", 60_000)));
            await seed.SaveChangesAsync();
        }

        await using (var del = _fixture.CreateDbContext())
        {
            var match = await del.Matches.FindAsync("m-jfc-3");
            del.Matches.Remove(match!);
            await del.SaveChangesAsync();
        }

        await using var db = _fixture.CreateDbContext();
        var repository = new JungleFirstClearRepository(db);
        (await repository.GetByMatchIdAsync("m-jfc-3", CancellationToken.None))
            .Should().BeEmpty("the FK cascade removes first clears with their match");
    }

    private static JungleFirstClear Clear(
        string matchId, int participantId, int? fullClearMs, params (string Camp, int TimestampMs)[] steps)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            FullClearTimeMs = fullClearMs,
            Steps = steps.Select(s => new JungleClearStep { Camp = s.Camp, TimestampMs = s.TimestampMs }).ToList()
        };
}
