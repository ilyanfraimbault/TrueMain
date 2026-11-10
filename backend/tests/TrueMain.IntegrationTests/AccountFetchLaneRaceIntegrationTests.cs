using AwesomeAssertions;
using Core.Lol.Identifiers;
using Core.Lol.Ranking;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #2448: AccountRefresh and Discovery hold accounts tracked across Riot round-trips while the
/// fetch lane writes the same rows in raw SQL. Under a whole-row <c>xmin</c> token that write
/// made the save match 0 rows: the slice rolled back and the rest of the run was dropped. The
/// two writers touch different columns, so both writes have to land.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AccountFetchLaneRaceIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Platform = "KR";
    private const string RacedPuuid = "puuid-raced";
    private const string NextPuuid = "puuid-next";

    private static readonly DateTime SeenInMatchUtc = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AccountRefresh_KeepsBothWritesAndFinishesTheRun_WhenTheFetchLaneWritesTheAccountMidSlice()
    {
        await SeedAccountAsync(RacedPuuid);
        await SeedAccountAsync(NextPuuid);

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient
            .GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(call => new RiotAccountDto { Puuid = call.Arg<string>(), GameName = "renamed", TagLine = "KR1" });

        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient
            .GetLeagueEntriesByPuuidAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (call.Arg<string>() == RacedPuuid)
                {
                    await WriteFromTheFetchLaneAsync(RacedPuuid);
                }

                return new List<RiotLeagueEntryByPuuidDto>
                {
                    new()
                    {
                        QueueType = "RANKED_SOLO_5x5",
                        Tier = "PLATINUM",
                        Rank = "II",
                        LeaguePoints = 64,
                        Wins = 30,
                        Losses = 20
                    }
                };
            });

        var process = new AccountRefreshProcess(
            NullLogger<AccountRefreshProcess>.Instance,
            accountClient,
            platformClient,
            fixture.CreateSessionFactory(),
            new RankSnapshotWriter(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new AccountRefreshOptions
            {
                BatchSize = 10,
                // One account per slice: the second one only runs if the first save survives.
                SaveBatchSize = 1
            }));

        await process.RunCoreAsync(RunInput.Whole, CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        var raced = await verify.RiotAccounts.AsNoTracking().SingleAsync(a => a.Puuid == RacedPuuid);
        raced.LastSeenInMatchAtUtc.Should().Be(SeenInMatchUtc, "the fetch lane's write must survive the save");
        raced.GameName.Should().Be("renamed");
        raced.Score.Should().BeGreaterThan(0);
        (await verify.RankSnapshots.CountAsync(s => s.RiotAccountId == raced.Id)).Should().Be(1);

        var next = await verify.RiotAccounts.AsNoTracking().SingleAsync(a => a.Puuid == NextPuuid);
        next.GameName.Should().Be("renamed", "the race must not drop the rest of the run");
    }

    [Fact]
    public async Task Discovery_KeepsBothWritesAndFinishesTheWindow_WhenTheFetchLaneWritesTheAccountMidSlice()
    {
        await SeedAccountAsync(RacedPuuid);

        var ladder = Substitute.For<ILadderDiscoveryService>();
        ladder
            .DiscoverSummonersAsync(
                Arg.Any<PlatformRoute>(),
                Arg.Any<DiscoveryOptions>(),
                Arg.Any<int>(),
                Arg.Any<ProfileFreshnessProbe>(),
                Arg.Any<CancellationToken>())
            .Returns(new LadderDiscoveryResult(
                [Discovered(RacedPuuid), Discovered(NextPuuid)],
                LadderSize: 2,
                AppliedOffset: 0,
                ProfileCallsSkipped: 0));

        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient
            .GetChampionMasteriesAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (call.Arg<string>() == RacedPuuid)
                {
                    await WriteFromTheFetchLaneAsync(RacedPuuid);
                }

                return new List<RiotChampionMasteryDto>();
            });

        var candidates = Substitute.For<ICandidateUpsertService>();
        candidates
            .UpsertAsync(
                Arg.Any<Ingestor.Persistence.IDataSession>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<RiotChampionMasteryDto>>(),
                Arg.Any<DiscoveryOptions>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(new CandidateUpsertResult(0, 0));

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            platformClient,
            fixture.CreateSessionFactory(),
            ladder,
            new AccountUpsertService(),
            candidates,
            new RankSnapshotWriter(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = [Platform],
                // One account per slice: the second one only runs if the first save survives.
                SaveBatchSize = 1,
                NewAccountsTarget = 0
            }));

        await process.RunCoreAsync(RunInput.Whole, CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        var raced = await verify.RiotAccounts.AsNoTracking().SingleAsync(a => a.Puuid == RacedPuuid);
        raced.LastSeenInMatchAtUtc.Should().Be(SeenInMatchUtc, "the fetch lane's write must survive the save");
        raced.Score.Should().BeGreaterThan(0);
        (await verify.RiotAccounts.AnyAsync(a => a.Puuid == NextPuuid))
            .Should().BeTrue("the race must not drop the rest of the window");
    }

    private async Task WriteFromTheFetchLaneAsync(string puuid)
    {
        await using var concurrent = fixture.CreateDbContext();
        await concurrent.RiotAccounts
            .Where(a => a.Puuid == puuid)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeenInMatchAtUtc, SeenInMatchUtc));
    }

    private async Task SeedAccountAsync(string puuid)
    {
        await using var seed = fixture.CreateDbContext();
        var longAgo = DateTime.UtcNow.AddDays(-30);
        seed.RiotAccounts.Add(new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            PlatformId = Platform,
            GameName = "old-name",
            TagLine = "KR1",
            SummonerId = $"summoner-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = longAgo,
            UpdatedAtUtc = longAgo
        });
        await seed.SaveChangesAsync();
    }

    private static DiscoveredSummoner Discovered(string puuid) => new(
        new RiotSummonerDto
        {
            Id = $"summoner-{puuid}",
            Puuid = puuid,
            Name = "ladder-player",
            ProfileIconId = 2,
            SummonerLevel = 101
        },
        new RankSnapshotInput(RankTier.Platinum, RankDivision.II, 75, Wins: 10, Losses: 5));
}
