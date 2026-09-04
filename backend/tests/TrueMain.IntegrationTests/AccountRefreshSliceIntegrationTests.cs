using AwesomeAssertions;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Core.Lol.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #1229 moved AccountRefresh's preload — the accounts and their latest rank snapshots —
/// inside each save slice, because both are mutated in place and the per-slice
/// <c>ClearTracking()</c> would otherwise detach everything a later slice still has to
/// write. A detached entity accepts property writes and persists none of them, so the
/// failure mode is silent data loss rather than an error.
/// <para>
/// The sibling cover for <c>DiscoveryProcess</c> pins the same property on its own loop;
/// this one exists because these are three independent loops (#1229 lists Discovery,
/// AccountRefresh and the participant harvest), and one of them can be "optimised" back to
/// a run-wide preload without the others noticing.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AccountRefreshSliceIntegrationTests(PostgresFixture fixture)
{
    private const string Platform = "KR";
    private static readonly string[] Puuids = ["refresh-slice-1", "refresh-slice-2"];

    [Fact]
    public async Task RunAsync_WithMultipleSaveSlices_PersistsTheInPlaceRankUpdateOfEverySlice()
    {
        await fixture.ResetDatabaseAsync();
        var nowUtc = DateTime.UtcNow;

        // Two accounts, each carrying a same-day snapshot at GOLD IV 10 LP. Same day is what
        // sends RankSnapshotWriter down its overwrite-in-place branch rather than the insert
        // one, and stale rank bookkeeping is what stops the freshness gate from skipping the
        // league call altogether.
        await using (var seed = fixture.CreateDbContext())
        {
            foreach (var puuid in Puuids)
            {
                var account = new RiotAccount
                {
                    Id = Guid.NewGuid(),
                    Puuid = puuid,
                    PlatformId = Platform,
                    GameName = "slice-player",
                    TagLine = "KR1",
                    SummonerId = $"summoner-{puuid}",
                    ProfileIconId = 1,
                    SummonerLevel = 100,
                    LastRankSyncAtUtc = nowUtc.AddDays(-2),
                    CreatedAtUtc = nowUtc.AddDays(-30),
                    UpdatedAtUtc = nowUtc.AddDays(-30)
                };
                seed.RiotAccounts.Add(account);
                seed.RankSnapshots.Add(new RankSnapshot
                {
                    Id = Guid.NewGuid(),
                    RiotAccountId = account.Id,
                    // Clamped into today: see TestInstants — an hour before 00:36 UTC is
                    // yesterday, and a snapshot on another day is appended, not updated.
                    CapturedAtUtc = TestInstants.EarlierSameUtcDay(TimeSpan.FromHours(1)),
                    Tier = "GOLD",
                    Division = "IV",
                    LeaguePoints = 10,
                    Wins = 1,
                    Losses = 1
                });
            }

            await seed.SaveChangesAsync();
        }

        var process = new AccountRefreshProcess(
            NullLogger<AccountRefreshProcess>.Instance,
            new StaticRiotAccountClient(),
            new PromotedRiotPlatformClient(),
            fixture.CreateSessionFactory(),
            new RankSnapshotWriter(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new AccountRefreshOptions
            {
                BatchSize = 10,
                // One account per slice, so the second account is preloaded and mutated after
                // the first slice's SaveChanges + ClearTracking.
                SaveBatchSize = 1
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        foreach (var puuid in Puuids)
        {
            var account = await verify.RiotAccounts.AsNoTracking().SingleAsync(a => a.Puuid == puuid);
            var snapshot = await verify.RankSnapshots.AsNoTracking()
                .SingleAsync(s => s.RiotAccountId == account.Id);

            // Seeded GOLD IV 10 LP, the ladder now says PLATINUM II 64 LP. Both rows are
            // mutated in place, so a drained tracker would leave either at its seeded value.
            snapshot.Tier.Should().Be("PLATINUM", "the snapshot update of {0} must survive the drain", puuid);
            snapshot.Division.Should().Be("II");
            snapshot.LeaguePoints.Should().Be(64);

            // The account is the other tracked entity the slice mutates: the writer stamps
            // its sync bookkeeping and the denormalised leaderboard sort key.
            account.LastRankSyncAtUtc.Should().NotBeNull().And.NotBe(nowUtc.AddDays(-2));
            account.Score.Should().BeGreaterThan(0);
        }

        // One row per account, not two: the same-day reading overwrites rather than appends.
        (await verify.RankSnapshots.CountAsync()).Should().Be(Puuids.Length);
    }

    /// <summary>Every account keeps the Riot ID it was seeded with.</summary>
    private sealed class StaticRiotAccountClient : IRiotAccountClient
    {
        public Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
            => Task.FromResult(new RiotAccountDto { Puuid = puuid, GameName = "slice-player", TagLine = "KR1" });

        public Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct)
            => Task.FromResult<RiotAccountDto?>(new RiotAccountDto { Puuid = "unused", GameName = gameName, TagLine = tagLine });
    }

    /// <summary>A ladder where everyone has been promoted since the seeded snapshot.</summary>
    private sealed class PromotedRiotPlatformClient : IRiotPlatformClient
    {
        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(
            PlatformRoute platform, string puuid, CancellationToken ct)
            => Task.FromResult(new List<RiotLeagueEntryByPuuidDto>
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
            });

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

        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<RiotLeagueDivisionEntryDto>> GetLeagueEntriesAsync(
            PlatformRoute platform, string queue, string tier, string division, int page, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
