using Core.Lol.Identifiers;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class DiscoveryProcessIntegrationTests
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
            new FakeLadderDiscoveryService(),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            new RankSnapshotWriter(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 1
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-discovered-1");

        account.PlatformId.Should().Be("KR");
        // GameName / TagLine are owned by AccountRefreshProcess via account-v1
        // — Discovery's upsert leaves them at the entity default so the next
        // refresh cycle backfills the identity from the authoritative source.
        // See issue #182.
        account.GameName.Should().BeEmpty();
        account.TagLine.Should().BeNull();
        account.SummonerId.Should().Be("summoner-discovered-1");
        account.ProfileIconId.Should().Be(23);
        account.SummonerLevel.Should().Be(201);
        account.LastProfileSyncAtUtc.Should().NotBeNull();
        account.LastRankSyncAtUtc.Should().NotBeNull();

        var snapshot = await verifyDb.RankSnapshots.SingleAsync(s => s.RiotAccountId == account.Id);
        snapshot.Tier.Should().Be("MASTER");
        snapshot.Division.Should().Be("I");
        snapshot.LeaguePoints.Should().Be(42);
        snapshot.Wins.Should().Be(7);
        snapshot.Losses.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_AdvancesAndWrapsTheSlidingWindowCursor_AcrossRuns()
    {
        await _fixture.ResetDatabaseAsync();

        var sessionFactory = _fixture.CreateSessionFactory();

        DiscoveryProcess BuildProcess() => new(
            NullLogger<DiscoveryProcess>.Instance,
            new FakeRiotPlatformClient(),
            sessionFactory,
            new FixedSizeLadderDiscoveryService(ladderSize: 5),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            new RankSnapshotWriter(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 0,
                MaxAccountsPerPlatformPerRun = 2,
                SlidingWindowEnabled = true
            }));

        // Window 2 over a ladder of 5: 0 -> 2 -> 4 -> wraps to 0.
        await BuildProcess().RunCoreAsync(CancellationToken.None);
        (await ReadCursorOffsetAsync("KR")).Should().Be(2);

        await BuildProcess().RunCoreAsync(CancellationToken.None);
        (await ReadCursorOffsetAsync("KR")).Should().Be(4);

        await BuildProcess().RunCoreAsync(CancellationToken.None);
        (await ReadCursorOffsetAsync("KR")).Should().Be(0);
    }

    private async Task<int?> ReadCursorOffsetAsync(string platformId)
    {
        await using var db = _fixture.CreateDbContext();
        var cursor = await db.DiscoveryCursors.AsNoTracking().SingleOrDefaultAsync(c => c.PlatformId == platformId);
        return cursor?.Offset;
    }

    [Fact]
    public async Task RunAsync_WhenLadderEntryHasNoRank_DoesNotWriteSnapshot()
    {
        await _fixture.ResetDatabaseAsync();

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            new FakeRiotPlatformClient(),
            _fixture.CreateSessionFactory(),
            new NoRankLadderDiscoveryService(),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            new RankSnapshotWriter(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 1
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-no-rank");
        account.LastRankSyncAtUtc.Should().BeNull();
        (await verifyDb.RankSnapshots.AnyAsync(s => s.RiotAccountId == account.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenLatestSnapshotMatchesLadder_DoesNotInsertDuplicate()
    {
        await _fixture.ResetDatabaseAsync();

        // Seed an existing account with a snapshot matching what the ladder will return.
        await using (var seedDb = _fixture.CreateDbContext())
        {
            var existing = new Data.Entities.RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = "puuid-discovered-1",
                PlatformId = "KR",
                GameName = "existing-player",
                SummonerId = "summoner-discovered-1",
                ProfileIconId = 1,
                SummonerLevel = 100,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-2)
            };
            seedDb.RiotAccounts.Add(existing);
            seedDb.RankSnapshots.Add(new Data.Entities.RankSnapshot
            {
                Id = Guid.NewGuid(),
                RiotAccountId = existing.Id,
                CapturedAtUtc = DateTime.UtcNow.AddHours(-1),
                Tier = "MASTER",
                Division = "I",
                LeaguePoints = 42,
                Wins = 5,
                Losses = 2
            });
            await seedDb.SaveChangesAsync();
        }

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            new FakeRiotPlatformClient(),
            _fixture.CreateSessionFactory(),
            new FakeLadderDiscoveryService(),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            new RankSnapshotWriter(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 0
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-discovered-1");
        account.LastRankSyncAtUtc.Should().NotBeNull();
        var snapshots = await verifyDb.RankSnapshots
            .Where(s => s.RiotAccountId == account.Id)
            .ToListAsync();
        snapshots.Should().ContainSingle("the ladder rank matches the existing latest snapshot, so no duplicate row is written");
    }

    [Fact]
    public async Task RunAsync_WhenOnePlatformFails_StillDiscoversRemainingPlatforms()
    {
        await _fixture.ResetDatabaseAsync();

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            new FakeRiotPlatformClient(),
            _fixture.CreateSessionFactory(),
            new PlatformFailingLadderDiscoveryService(failingPlatform: "EUW1"),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            new RankSnapshotWriter(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["EUW1", "KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 1
            }));

        // Issue #443 follow-up: an EUW1 ladder stuck behind a Riot 429 backoff
        // must not abort KR/NA1 discovery.
        var payload = await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-discovered-1");
        account.PlatformId.Should().Be("KR");

        // The recorded run detail names the failed platform and its error.
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        json.Should().Contain("EUW1").And.Contain("simulated ladder outage");
    }

    [Fact]
    public async Task RunAsync_WhenAllPlatformsFail_ThrowsSoTheRunIsRecordedAsFailed()
    {
        await _fixture.ResetDatabaseAsync();

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            new FakeRiotPlatformClient(),
            _fixture.CreateSessionFactory(),
            new PlatformFailingLadderDiscoveryService(failingPlatform: null),
            new AccountUpsertService(),
            new NoOpCandidateUpsertService(),
            new RankSnapshotWriter(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["EUW1", "KR"],
                SaveBatchSize = 1,
                NewAccountsTarget = 1
            }));

        var act = async () => await process.RunCoreAsync(CancellationToken.None);

        // Nothing was discovered anywhere: surface the failure instead of
        // recording an empty success.
        (await act.Should().ThrowAsync<AggregateException>())
            .Which.Message.Should().Contain("EUW1").And.Contain("KR");
    }

    /// <summary>
    /// Throws for the given platform (or every platform when <c>null</c>), and
    /// otherwise returns the same ladder data as <see cref="FakeLadderDiscoveryService"/>.
    /// </summary>
    private sealed class PlatformFailingLadderDiscoveryService(string? failingPlatform) : ILadderDiscoveryService
    {
        private readonly FakeLadderDiscoveryService _inner = new();

        public Task<LadderDiscoveryResult> DiscoverSummonersAsync(
            PlatformRoute platform,
            DiscoveryOptions options,
            int offset,
            CancellationToken ct)
        {
            if (failingPlatform is null || platform.ToString() == failingPlatform)
            {
                throw new InvalidOperationException("simulated ladder outage");
            }

            return _inner.DiscoverSummonersAsync(platform, options, offset, ct);
        }
    }

    private sealed class NoRankLadderDiscoveryService : ILadderDiscoveryService
    {
        public Task<LadderDiscoveryResult> DiscoverSummonersAsync(
            PlatformRoute platform,
            DiscoveryOptions options,
            int offset,
            CancellationToken ct)
        {
            var discovered = new List<DiscoveredSummoner>
            {
                new(
                    new RiotSummonerDto
                    {
                        Id = "summoner-no-rank",
                        Puuid = "puuid-no-rank",
                        Name = "no-rank-player",
                        ProfileIconId = 5,
                        SummonerLevel = 50
                    },
                    Rank: null)
            };

            return Task.FromResult(new LadderDiscoveryResult(discovered, discovered.Count, offset));
        }
    }

    private sealed class FakeLadderDiscoveryService : ILadderDiscoveryService
    {
        public Task<LadderDiscoveryResult> DiscoverSummonersAsync(
            PlatformRoute platform,
            DiscoveryOptions options,
            int offset,
            CancellationToken ct)
        {
            var discovered = new List<DiscoveredSummoner>
            {
                new(
                    new RiotSummonerDto
                    {
                        Id = "summoner-discovered-1",
                        Puuid = "puuid-discovered-1",
                        Name = "discovered-player",
                        ProfileIconId = 23,
                        SummonerLevel = 201
                    },
                    new RankSnapshotInput("MASTER", "I", 42, Wins: 7, Losses: 3))
            };

            return Task.FromResult(new LadderDiscoveryResult(discovered, discovered.Count, offset));
        }
    }

    /// <summary>
    /// Returns one summoner per run but reports a fixed ladder size and the applied
    /// offset (offset % size), so the cursor advance/wrap math in DiscoveryProcess can
    /// be exercised without a real ladder.
    /// </summary>
    private sealed class FixedSizeLadderDiscoveryService(int ladderSize) : ILadderDiscoveryService
    {
        public Task<LadderDiscoveryResult> DiscoverSummonersAsync(
            PlatformRoute platform,
            DiscoveryOptions options,
            int offset,
            CancellationToken ct)
        {
            var discovered = new List<DiscoveredSummoner>
            {
                new(
                    new RiotSummonerDto
                    {
                        Id = "summoner-cursor",
                        Puuid = "puuid-cursor",
                        ProfileIconId = 1,
                        SummonerLevel = 30
                    },
                    Rank: null)
            };

            var applied = options.SlidingWindowEnabled && ladderSize > 0 ? offset % ladderSize : 0;
            return Task.FromResult(new LadderDiscoveryResult(discovered, ladderSize, applied));
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

        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
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

}
