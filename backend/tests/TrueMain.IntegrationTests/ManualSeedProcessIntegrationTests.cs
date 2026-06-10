using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ManualSeedProcessIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ManualSeedProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ResolvableRiotId_UpsertsAccountAndQueuesCandidatesAndMarksIngested()
    {
        await _fixture.ResetDatabaseAsync();

        var requestId = Guid.NewGuid();
        await SeedRequestAsync(requestId, "Phantasm", "EUW1", "EUW1");

        var process = BuildProcess(
            new FakeRiotAccountClient(("Phantasm", "EUW1", "puuid-seed-1")),
            new FakeRiotPlatformClient(
                summonerId: "summoner-seed-1",
                masteries:
                [
                    Mastery(championId: 64, points: 500_000, daysAgo: 1),
                    Mastery(championId: 157, points: 300_000, daysAgo: 2)
                ]));

        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var account = await db.RiotAccounts.SingleAsync(a => a.Puuid == "puuid-seed-1");
        account.PlatformId.Should().Be("EUW1");
        account.SummonerId.Should().Be("summoner-seed-1");
        // The seed flow backfills the authoritative Riot ID from account-v1
        // (unlike Discovery, which leaves it blank).
        account.GameName.Should().Be("Phantasm");
        account.TagLine.Should().Be("EUW1");

        var candidates = await db.MainCandidates
            .Where(c => c.Puuid == "puuid-seed-1")
            .ToListAsync();
        candidates.Should().HaveCount(2);
        candidates.Should().OnlyContain(c => c.Status == MainCandidateStatus.Queued,
            "seeded candidates are promoted straight to Queued, skipping Scoring");
        candidates.Select(c => c.ChampionId).Should().BeEquivalentTo([64, 157]);

        var request = await db.SeedRequests.SingleAsync(r => r.Id == requestId);
        request.Status.Should().Be(SeedRequestStatus.Ingested);
        request.Error.Should().BeNull();
        request.ResolvedPuuid.Should().Be("puuid-seed-1");
        request.ResolvedRiotAccountId.Should().Be(account.Id);
        request.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_UnresolvableRiotId_MarksFailedWithoutCreatingAccount()
    {
        await _fixture.ResetDatabaseAsync();

        var requestId = Guid.NewGuid();
        await SeedRequestAsync(requestId, "Ghost", "NA1", "NA1");

        // No tuples => the fake resolves every Riot ID to null (404 / not found).
        var process = BuildProcess(
            new FakeRiotAccountClient(),
            new FakeRiotPlatformClient(summonerId: "unused", masteries: []));

        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        (await db.RiotAccounts.AnyAsync()).Should().BeFalse();
        (await db.MainCandidates.AnyAsync()).Should().BeFalse();

        var request = await db.SeedRequests.SingleAsync(r => r.Id == requestId);
        request.Status.Should().Be(SeedRequestStatus.Failed);
        request.Error.Should().Be("Riot ID not found");
        request.ProcessedAtUtc.Should().NotBeNull();
        request.ResolvedPuuid.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_RiotThrows_MarksFailedWithTruncatedError()
    {
        await _fixture.ResetDatabaseAsync();

        var requestId = Guid.NewGuid();
        await SeedRequestAsync(requestId, "Phantasm", "EUW1", "EUW1");

        var process = BuildProcess(
            new ThrowingRiotAccountClient("riot exploded"),
            new FakeRiotPlatformClient(summonerId: "unused", masteries: []));

        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var request = await db.SeedRequests.SingleAsync(r => r.Id == requestId);
        request.Status.Should().Be(SeedRequestStatus.Failed);
        request.Error.Should().Contain("riot exploded");
        request.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_CancelledAfterClaim_ResetsRequestToPendingAndRethrows()
    {
        await _fixture.ResetDatabaseAsync();

        var requestId = Guid.NewGuid();
        await SeedRequestAsync(requestId, "Phantasm", "EUW1", "EUW1");

        // Cancels the run from inside the resolution step — i.e. after the request
        // has been claimed (flipped to Resolving) but before it reaches a terminal
        // state, exercising the OperationCanceledException recovery path.
        using var cts = new CancellationTokenSource();
        var process = BuildProcess(
            new CancellingRiotAccountClient(cts),
            new FakeRiotPlatformClient(summonerId: "unused", masteries: []));

        var act = async () => await process.RunCoreAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await using var db = _fixture.CreateDbContext();
        var request = await db.SeedRequests.SingleAsync(r => r.Id == requestId);
        // Reset to Pending so a later run can re-claim it, rather than stranded
        // forever in Resolving.
        request.Status.Should().Be(SeedRequestStatus.Pending);
        request.ProcessedAtUtc.Should().BeNull();
    }

    private ManualSeedProcess BuildProcess(IRiotAccountClient accountClient, IRiotPlatformClient platformClient)
        => new(
            NullLogger<ManualSeedProcess>.Instance,
            accountClient,
            platformClient,
            _fixture.CreateSessionFactory(),
            new AccountUpsertService(),
            new CandidateUpsertService(),
            Microsoft.Extensions.Options.Options.Create(new ManualSeedOptions
            {
                BatchSize = 25,
                TopChampionsPerAccount = 10,
                MaxLastPlayDays = 0
            }));

    private async Task SeedRequestAsync(Guid id, string gameName, string tagLine, string platformId)
    {
        await using var db = _fixture.CreateDbContext();
        db.SeedRequests.Add(new SeedRequest
        {
            Id = id,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            Status = SeedRequestStatus.Pending,
            RequestedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static RiotChampionMasteryDto Mastery(int championId, long points, int daysAgo)
        => new()
        {
            ChampionId = championId,
            ChampionPoints = points,
            // champion-mastery lastPlayTime is epoch milliseconds.
            LastPlayTime = new DateTimeOffset(DateTime.UtcNow.AddDays(-daysAgo)).ToUnixTimeMilliseconds()
        };

    private sealed class FakeRiotAccountClient(params (string GameName, string TagLine, string Puuid)[] known)
        : IRiotAccountClient
    {
        public Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct)
        {
            var match = known.FirstOrDefault(entry =>
                string.Equals(entry.GameName, gameName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.TagLine, tagLine, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult<RiotAccountDto?>(
                match.Puuid is null
                    ? null
                    : new RiotAccountDto { Puuid = match.Puuid, GameName = match.GameName, TagLine = match.TagLine });
        }

        public Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingRiotAccountClient(string message) : IRiotAccountClient
    {
        public Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct)
            => throw new InvalidOperationException(message);

        public Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class CancellingRiotAccountClient(CancellationTokenSource cts) : IRiotAccountClient
    {
        public Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct)
        {
            // Simulate the run being cancelled (host shutdown) mid-resolution.
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }

        public Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeRiotPlatformClient(string summonerId, List<RiotChampionMasteryDto> masteries)
        : IRiotPlatformClient
    {
        public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => Task.FromResult(new RiotSummonerDto
            {
                Id = summonerId,
                Puuid = puuid,
                Name = string.Empty,
                ProfileIconId = 7,
                SummonerLevel = 321
            });

        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => Task.FromResult(masteries);

        public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
