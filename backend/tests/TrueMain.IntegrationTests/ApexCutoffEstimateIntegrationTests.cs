using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The one-off estimate of past apex cutoffs from the tracked accounts' rank snapshots (#2181),
/// against Postgres: it is one hand-written statement, so the day bucketing, the sample floor,
/// the measured-day precedence and the run-once guard are only provable on a real database.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ApexCutoffEstimateIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime NowUtc = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

    private readonly PostgresFixture _fixture;

    public ApexCutoffEstimateIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task EstimatesEachPastDayFromItsLowestTrackedSnapshotOnce()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            // Three days back: twelve Grandmaster players, the lowest at 410 LP.
            Seed(db, "EUW1", "GRANDMASTER", NowUtc.AddDays(-3), Enumerable.Range(0, 12).Select(i => 410 + (i * 15)));
            // Two days back: measured already — the estimate must not touch it.
            Seed(db, "EUW1", "GRANDMASTER", NowUtc.AddDays(-2), Enumerable.Range(0, 12).Select(i => 300 + (i * 15)));
            db.ApexCutoffSnapshots.Add(new ApexCutoffSnapshot
            {
                PlatformId = "EUW1",
                Tier = RankTier.Grandmaster,
                CapturedOnUtc = DateOnly.FromDateTime(NowUtc.AddDays(-2)),
                MinLeaguePoints = 395,
                EntryCount = 700,
                UpdatedAtUtc = NowUtc.AddDays(-2),
            });
            // Yesterday: too thin a sample to say anything.
            Seed(db, "EUW1", "GRANDMASTER", NowUtc.AddDays(-1), [380, 390, 400]);
            // Today: left to the day's measurement.
            Seed(db, "EUW1", "CHALLENGER", NowUtc.AddHours(-2), Enumerable.Range(0, 12).Select(i => 900 + i));
            // A Master snapshot never feeds a cutoff.
            Seed(db, "EUW1", "MASTER", NowUtc.AddDays(-3), Enumerable.Range(0, 12).Select(i => 10 + i));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var written = await new ApexCutoffSnapshotRepository(db)
                .EstimateHistoryAsync(NowUtc.AddDays(-30), NowUtc, minSnapshots: 10, CancellationToken.None);
            written.Should().Be(1);
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var rows = await db.ApexCutoffSnapshots.AsNoTracking().OrderBy(row => row.CapturedOnUtc).ToListAsync();
            rows.Select(row => (row.CapturedOnUtc, row.Tier, row.MinLeaguePoints, row.EntryCount, row.IsEstimated)).Should().Equal(
                (DateOnly.FromDateTime(NowUtc.AddDays(-3)), RankTier.Grandmaster, 410, 12, true),
                (DateOnly.FromDateTime(NowUtc.AddDays(-2)), RankTier.Grandmaster, 395, 700, false));

            // Once an estimate exists, the history is done: no second scan, nothing written.
            var again = await new ApexCutoffSnapshotRepository(db)
                .EstimateHistoryAsync(NowUtc.AddDays(-30), NowUtc, minSnapshots: 1, CancellationToken.None);
            again.Should().Be(0);
        }
    }

    [Fact]
    public async Task AMeasurementReplacesAnEstimate()
    {
        var day = DateOnly.FromDateTime(NowUtc);
        await using (var db = _fixture.CreateDbContext())
        {
            db.ApexCutoffSnapshots.Add(new ApexCutoffSnapshot
            {
                PlatformId = "KR",
                Tier = RankTier.Challenger,
                CapturedOnUtc = day,
                MinLeaguePoints = 1200,
                EntryCount = 40,
                UpdatedAtUtc = NowUtc.AddHours(-3),
                IsEstimated = true,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            await new ApexCutoffSnapshotRepository(db)
                .UpsertAsync("KR", RankTier.Challenger, 1150, 300, NowUtc, CancellationToken.None);
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var row = await db.ApexCutoffSnapshots.AsNoTracking().SingleAsync();
            (row.MinLeaguePoints, row.EntryCount, row.IsEstimated).Should().Be((1150, 300, false));
        }
    }

    private static void Seed(Data.TrueMainDbContext db, string platformId, string tier, DateTime capturedAtUtc, IEnumerable<int> leaguePoints)
    {
        foreach (var lp in leaguePoints)
        {
            var account = new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = Guid.NewGuid().ToString("N"),
                GameName = "Apex",
                TagLine = platformId,
                PlatformId = platformId,
                ProfileIconId = 1,
                SummonerLevel = 100,
                CreatedAtUtc = NowUtc.AddDays(-365),
                UpdatedAtUtc = NowUtc,
            };
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(new RankSnapshot
            {
                Id = Guid.NewGuid(),
                RiotAccountId = account.Id,
                CapturedAtUtc = capturedAtUtc,
                Tier = RankTiers.ParseTier(tier),
                Division = RankDivision.I,
                LeaguePoints = lp,
            });
        }
    }
}
