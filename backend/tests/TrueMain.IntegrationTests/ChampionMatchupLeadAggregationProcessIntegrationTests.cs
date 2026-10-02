using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionMatchupLeadAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const string Position = "MIDDLE";

    private readonly PostgresFixture _fixture;

    public ChampionMatchupLeadAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_AggregatesMatchupsFromRawRows()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var matchup = await db.ChampionMatchupStats.AsNoTracking().SingleAsync();
        matchup.ChampionId.Should().Be(Champion);
        matchup.TeamPosition.Should().Be(Position);
        matchup.OpponentChampionId.Should().Be(Opponent);
        matchup.Patch.Should().Be("16.4", "the raw GameVersion folds to major.minor");
        matchup.Games.Should().Be(12);
        matchup.Wins.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_ReplacesByScopeWithoutDuplicatesOnRerun()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionMatchupStats.CountAsync()).Should().Be(1, "re-running deletes the live scope then reinserts");

        var matchup = await db.ChampionMatchupStats.AsNoTracking().SingleAsync();
        matchup.Games.Should().Be(12, "counts must not double on a second run");
    }

    [Fact]
    public async Task RunAsync_KeepsAggregatesForPatchesWhoseMatchesWerePurged()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync(games: 12, version: "16.4.521.123", wins: 7);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        // Simulate MatchDataRetention dropping the 16.4 match data, then a fresh
        // 16.5 patch arriving.
        await using (var mutate = _fixture.CreateDbContext())
        {
            mutate.MatchParticipants.RemoveRange(await mutate.MatchParticipants.ToListAsync());
            mutate.Matches.RemoveRange(await mutate.Matches.ToListAsync());
            await mutate.SaveChangesAsync();
        }

        await SeedGamesAsync(games: 11, version: "16.5.1", wins: 4, matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var matchups = await db.ChampionMatchupStats.AsNoTracking().ToListAsync();

        // The 16.4 row is frozen (its matches are gone so it can't be rebuilt) and
        // the live 16.5 patch produces its own row. Neither is wiped.
        matchups.Select(m => m.Patch).Should().BeEquivalentTo(["16.4", "16.5"]);
        matchups.Single(m => m.Patch == "16.4").Games.Should().Be(12);
        matchups.Single(m => m.Patch == "16.5").Games.Should().Be(11);
    }

    private ChampionMatchupLeadAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionMatchupLeadAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    private async Task SeedGamesAsync(int games, string version, int wins, string matchPrefix = "m")
    {
        await using var db = _fixture.CreateDbContext();

        // First seed creates the tracked account; the freeze test's second seed
        // reuses it (its Id is set client-side by the builder, so it is usable
        // before SaveChanges for the participant rows below).
        var account = await db.RiotAccounts.FirstOrDefaultAsync();
        if (account is null)
        {
            account = new RiotAccountBuilder()
                .WithGameName("AggMain")
                .WithTagLine("KR1")
                .WithPuuid("agg-main-puuid")
                .Build();
            db.RiotAccounts.Add(account);
        }

        for (var i = 0; i < games; i++)
        {
            var matchId = $"{matchPrefix}-{version}-{i}";
            var match = new MatchBuilder().WithId(matchId).WithQueueId(QueueId).WithGameVersion(version).Build();
            db.Matches.Add(match);

            db.MatchParticipants.Add(Participant(matchId, 1, Champion, teamId: 100, win: i < wins, riotAccountId: account.Id));
            db.MatchParticipants.Add(Participant(matchId, 2, Opponent, teamId: 200, win: i >= wins));
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, bool win, Guid? riotAccountId = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = win,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = []
        };
}
