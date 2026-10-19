using AwesomeAssertions;
using Data.Entities;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The champion game count rollup end to end (#2132). It replaces a live count of
/// <c>match_participants</c>, so what it has to get right is staying that count: a slice
/// recomputed from the raw rows on every run, a patch retention deleted no longer counted,
/// and a participant the lane fix moved counted under its new position.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionGameCountRefreshProcessIntegrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await postgres.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunCoreAsync_CountsStoredParticipantsPerSlice()
    {
        await SeedAsync();

        var summary = await BuildProcess().RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<ChampionGameCountRefreshSummary>()
            .Which.Should().BeEquivalentTo(new ChampionGameCountRefreshSummary(
                Slices: 4, SlicesRemoved: 0, Rows: 5, Games: 6));

        var rows = await LoadAsync();
        Games(rows, "EUW1", "16.4", 420, "MIDDLE", 22).Should().Be(2);
        Games(rows, "EUW1", "16.4", 420, "JUNGLE", 64).Should().Be(1);
        Games(rows, "KR", "16.4", 420, "MIDDLE", 22).Should().Be(1);
        Games(rows, "EUW1", "16.5", 440, "BOTTOM", 22).Should().Be(1);
        Games(rows, "EUW1", "", 420, "TOP", 7).Should()
            .Be(1, "an unparseable game version has no patch, and is stored under the empty string");
    }

    [Fact]
    public async Task RunCoreAsync_FollowsRetentionAndTheLaneFix_OnTheNextRun()
    {
        await SeedAsync();
        await BuildProcess().RunCoreAsync(CancellationToken.None);

        await using (var db = postgres.CreateDbContext())
        {
            // Retention drops the whole 16.5 flex slice, and the lane fix moves one of the
            // two EUW1 16.4 MIDDLE games to TOP.
            await db.MatchParticipants.Where(p => p.MatchId == "GC_3").ExecuteDeleteAsync();
            await db.Matches.Where(m => m.Id == "GC_3").ExecuteDeleteAsync();
            await db.MatchParticipants
                .Where(p => p.MatchId == "GC_2" && p.ChampionId == 22)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.TeamPosition, "TOP"));
        }

        var summary = await BuildProcess().RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<ChampionGameCountRefreshSummary>()
            .Which.SlicesRemoved.Should().Be(1);

        var rows = await LoadAsync();
        rows.Should().NotContain(row => row.Patch == "16.5", "retention deleted every match of that slice");
        Games(rows, "EUW1", "16.4", 420, "MIDDLE", 22).Should().Be(1);
        Games(rows, "EUW1", "16.4", 420, "TOP", 22).Should().Be(1);
        rows.Sum(row => row.Games).Should().Be(5);
    }

    [Fact]
    public async Task RunCoreAsync_SkipsWithinTheMinimumInterval()
    {
        await SeedAsync();
        var runs = new FakeProcessRunStore { LastCompletedRunStartUtc = DateTime.UtcNow.AddMinutes(-5) };

        var summary = await BuildProcess(runs).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<SkippedSummary>();
        (await LoadAsync()).Should().BeEmpty();
    }

    private ChampionGameCountRefreshProcess BuildProcess(FakeProcessRunStore? runs = null)
        => new(
            NullLogger<ChampionGameCountRefreshProcess>.Instance,
            new TestDbContextFactory(postgres),
            runs ?? new FakeProcessRunStore(),
            TimeProvider.System);

    private async Task<List<ChampionGameCount>> LoadAsync()
    {
        await using var db = postgres.CreateDbContext();
        return await db.ChampionGameCounts.AsNoTracking().ToListAsync();
    }

    private static long Games(
        IEnumerable<ChampionGameCount> rows,
        string platformId,
        string patch,
        int queueId,
        string position,
        int championId)
        => rows.Single(row => row.PlatformId == platformId
                && row.Patch == patch
                && row.QueueId == queueId
                && row.TeamPosition == position
                && row.ChampionId == championId)
            .Games;

    private async Task SeedAsync()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        await using var db = postgres.CreateDbContext();

        db.Matches.AddRange(
            Match("GC_1", "EUW1", "16.4.1", 420, start),
            Match("GC_2", "EUW1", "16.4.2", 420, start),
            Match("GC_3", "EUW1", "16.5.1", 440, start),
            Match("GC_4", "KR", "16.4.1", 420, start),
            Match("GC_5", "EUW1", "not-a-version", 420, start));

        db.MatchParticipants.AddRange(
            Participant("GC_1", 1, 22, "MIDDLE"),
            Participant("GC_1", 2, 64, "JUNGLE"),
            Participant("GC_2", 1, 22, "MIDDLE"),
            Participant("GC_3", 1, 22, "BOTTOM"),
            Participant("GC_4", 1, 22, "MIDDLE"),
            Participant("GC_5", 1, 7, "TOP"));

        await db.SaveChangesAsync();
    }

    private static Match Match(string id, string platformId, string gameVersion, int queueId, DateTime startUtc)
        => new MatchBuilder()
            .WithId(id)
            .WithPlatformId(platformId)
            .WithQueueId(queueId)
            .WithGameVersion(gameVersion)
            .WithGameStartTimeUtc(startUtc)
            .WithCreatedAtUtc(startUtc)
            .Build();

    private static MatchParticipant Participant(string matchId, int participantId, int championId, string position)
        => new MatchParticipantBuilder()
            .WithId(Guid.NewGuid())
            .WithMatchId(matchId)
            .WithParticipantId(participantId)
            .WithPuuid($"gc-{matchId}-{participantId}")
            .WithSummonerName("gc")
            .WithChampionId(championId)
            .WithPosition(position)
            .Build();
}
