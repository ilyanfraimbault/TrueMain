using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #900: mains that stopped playing must leave the tracked pool, and come back on their
/// own when their player returns — both decided from champion mastery, without spending
/// a single match-v5 call.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MainActivityProcessIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MainActivityProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldDeactivateMain_WhenMasteryLastPlayIsStale()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMainAsync("puuid-idle-1", championId: 22, isActive: true, lastActivityCheckAtUtc: null);

        var process = BuildProcess(new FakeRiotPlatformClient(Mastery(22, daysAgo: 90)));

        var summary = await process.RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<MainActivitySummary>()
            .Which.MainsDeactivated.Should().Be(1);

        await using var db = _fixture.CreateDbContext();
        var stat = db.MainChampionStats.Single(s => s.Puuid == "puuid-idle-1");
        stat.IsActive.Should().BeFalse();

        // The row survives: history stays readable and the player is one mastery
        // check away from coming back, instead of having to be rediscovered.
        stat.IsMain.Should().BeTrue();
        db.RiotAccounts.Single(a => a.Puuid == "puuid-idle-1").LastActivityCheckAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_ShouldReactivateMain_WhenThePlayerCameBack()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMainAsync("puuid-back-1", championId: 22, isActive: false, lastActivityCheckAtUtc: DateTime.UtcNow.AddDays(-10));

        var process = BuildProcess(new FakeRiotPlatformClient(Mastery(22, daysAgo: 1)));

        var summary = await process.RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<MainActivitySummary>()
            .Which.MainsReactivated.Should().Be(1);

        await using var db = _fixture.CreateDbContext();
        db.MainChampionStats.Single(s => s.Puuid == "puuid-back-1").IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ShouldOnlyRetireTheChampionThatWasDropped()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMainAsync("puuid-mixed-1", championId: 22, isActive: true, lastActivityCheckAtUtc: null);
        await AddMainStatAsync("puuid-mixed-1", championId: 51);

        var process = BuildProcess(new FakeRiotPlatformClient(
            Mastery(22, daysAgo: 90),
            Mastery(51, daysAgo: 2)));

        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var stats = db.MainChampionStats
            .Where(s => s.Puuid == "puuid-mixed-1")
            .ToDictionary(s => s.ChampionId, s => s.IsActive);

        stats[22].Should().BeFalse();
        stats[51].Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ShouldLeaveTheMainUntouched_WhenTheMasteryCallFails()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMainAsync("puuid-flaky-1", championId: 22, isActive: true, lastActivityCheckAtUtc: null);

        var process = BuildProcess(new ThrowingRiotPlatformClient());

        var summary = await process.RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<MainActivitySummary>()
            .Which.AccountsFailed.Should().Be(1);

        await using var db = _fixture.CreateDbContext();

        // A failed lookup is not evidence of inactivity, and the account keeps a null
        // check stamp so the next run retries it first.
        db.MainChampionStats.Single(s => s.Puuid == "puuid-flaky-1").IsActive.Should().BeTrue();
        db.RiotAccounts.Single(a => a.Puuid == "puuid-flaky-1").LastActivityCheckAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ShouldStampTheCheck_WhenThePlatformCannotBeParsed()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMainAsync(
            "puuid-corrupt-1", championId: 22, isActive: true, lastActivityCheckAtUtc: null, platformId: "XX9");

        var process = BuildProcess(new FakeRiotPlatformClient(Mastery(22, daysAgo: 1)));

        var summary = await process.RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<MainActivitySummary>()
            .Which.AccountsSkipped.Should().Be(1);

        await using var db = _fixture.CreateDbContext();

        // The opposite of the transient failure above (#1223): no future run makes an
        // unparseable platform_id parse, and the selection is ordered by this very
        // column, so leaving it null parks the row at the head of every batch and burns
        // the slot of an account that could actually be checked.
        db.RiotAccounts.Single(a => a.Puuid == "puuid-corrupt-1").LastActivityCheckAtUtc.Should().NotBeNull();

        // Nothing was learned about the player, so no verdict may be applied.
        db.MainChampionStats.Single(s => s.Puuid == "puuid-corrupt-1").IsActive.Should().BeTrue();
    }

    private MainActivityProcess BuildProcess(IRiotPlatformClient platformClient)
        => new(
            NullLogger<MainActivityProcess>.Instance,
            platformClient,
            _fixture.CreateSessionFactory(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new MainActivityOptions
            {
                BatchSize = 50,
                InactiveAfterDays = 30,
                RecheckAfterHours = 24
            }));

    private async Task SeedMainAsync(
        string puuid,
        int championId,
        bool isActive,
        DateTime? lastActivityCheckAtUtc,
        string platformId = "KR")
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = puuid,
            GameName = puuid,
            TagLine = "KR1",
            PlatformId = platformId,
            SummonerId = $"summoner-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 200,
            CreatedAtUtc = now.AddDays(-60),
            UpdatedAtUtc = now.AddDays(-1),
            LastActivityCheckAtUtc = lastActivityCheckAtUtc
        });

        db.MainChampionStats.Add(NewStat(puuid, championId, isActive, platformId));
        await db.SaveChangesAsync();
    }

    private async Task AddMainStatAsync(string puuid, int championId)
    {
        await using var db = _fixture.CreateDbContext();
        db.MainChampionStats.Add(NewStat(puuid, championId, isActive: true));
        await db.SaveChangesAsync();
    }

    private static MainChampionStat NewStat(string puuid, int championId, bool isActive, string platformId = "KR")
        => new()
        {
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 30,
            ChampionMatches = 25,
            PlayRate = 0.83,
            IsMain = true,
            IsActive = isActive,
            IsOtp = false,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [],
            CalculatedAtUtc = DateTime.UtcNow.AddDays(-2)
        };

    private static RiotChampionMasteryDto Mastery(int championId, int daysAgo)
        => new()
        {
            ChampionId = championId,
            ChampionPoints = 500_000,
            // champion-mastery lastPlayTime is epoch milliseconds.
            LastPlayTime = new DateTimeOffset(DateTime.UtcNow.AddDays(-daysAgo)).ToUnixTimeMilliseconds()
        };

    private sealed class FakeRiotPlatformClient(params RiotChampionMasteryDto[] masteries) : IRiotPlatformClient
    {
        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => Task.FromResult(masteries.ToList());

        public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingRiotPlatformClient : IRiotPlatformClient
    {
        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => Task.FromException<List<RiotChampionMasteryDto>>(new HttpRequestException("mastery boom"));

        public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
