using Core;
using FluentAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

public sealed class DiscoveryProcessIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public DiscoveryProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldPersistDiscoveredAccountsThroughTheProcessPath()
    {
        await _fixture.ResetDatabaseAsync();

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            new FakeRiotPlatformClient(),
            _fixture.CreateSessionFactory(),
            new FakeProcessRunRecorder(),
            new FakeLadderDiscoveryService(),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 1
            }));

        await process.RunAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-discovered-1");

        account.PlatformId.Should().Be("KR");
        account.GameName.Should().Be("discovered-player");
        account.SummonerId.Should().Be("summoner-discovered-1");
        account.ProfileIconId.Should().Be(23);
        account.SummonerLevel.Should().Be(201);
        account.LastProfileSyncAtUtc.Should().NotBeNull();
    }

    private sealed class FakeLadderDiscoveryService : ILadderDiscoveryService
    {
        public Task<List<RiotSummonerDto>> DiscoverSummonersAsync(
            PlatformRoute platform,
            DiscoveryOptions options,
            CancellationToken ct)
        {
            return Task.FromResult(new List<RiotSummonerDto>
            {
                new()
                {
                    Id = "summoner-discovered-1",
                    Puuid = "puuid-discovered-1",
                    Name = "discovered-player",
                    ProfileIconId = 23,
                    SummonerLevel = 201
                }
            });
        }
    }

    private sealed class FakeRiotPlatformClient : IRiotPlatformClient
    {
        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => Task.FromResult(new List<RiotChampionMasteryDto>());

        public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class NoOpCandidateUpsertService : ICandidateUpsertService
    {
        public Task<CandidateUpsertResult> UpsertAsync(
            Data.Repositories.IDataSession session,
            string platformId,
            string puuid,
            IReadOnlyCollection<RiotChampionMasteryDto> masteries,
            DiscoveryOptions options,
            DateTime nowUtc,
            CancellationToken ct)
            => Task.FromResult(new CandidateUpsertResult(0, 0));
    }

    private sealed class FakeProcessRunRecorder : IProcessRunRecorder
    {
        public Task RecordAsync(
            string processName,
            DateTime startedAtUtc,
            DateTime finishedAtUtc,
            Data.Entities.ProcessRunStatus status,
            object? summary,
            string? error,
            CancellationToken ct)
            => Task.CompletedTask;
    }
}
