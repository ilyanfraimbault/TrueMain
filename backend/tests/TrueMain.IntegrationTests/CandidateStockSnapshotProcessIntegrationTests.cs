using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The snapshot step end to end (#1403), over both real databases: the grouped count is
/// an EF <c>GroupBy</c> that has to translate to SQL — a client-side fallback would load
/// every candidate row into memory, which is how #600 took the VPS down — and the
/// documents it produces are what the panel later reads.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CandidateStockSnapshotProcessIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
{
    [Fact]
    public async Task RunCoreAsync_RecordsOneReadingPerPlatformAndStatus()
    {
        await postgres.ResetDatabaseAsync();
        await mongo.ResetAsync();

        await using (var db = postgres.CreateDbContext())
        {
            db.MainCandidates.AddRange(
                Candidate("euw-a", 1, "EUW1", MainCandidateStatus.Queued),
                Candidate("euw-b", 2, "EUW1", MainCandidateStatus.Queued),
                Candidate("euw-c", 3, "EUW1", MainCandidateStatus.Validated),
                Candidate("kr-a", 4, "KR", MainCandidateStatus.Scored));
            await db.SaveChangesAsync();
        }

        using var context = BuildContext();
        var store = new CandidateStockSnapshotStore(context);
        var summary = await BuildProcess(store).RunCoreAsync(CancellationToken.None);

        var statusCount = Enum.GetValues<MainCandidateStatus>().Length;
        summary.Should().BeOfType<CandidateStockSnapshotSummary>()
            .Which.Should().BeEquivalentTo(new CandidateStockSnapshotSummary(
                Platforms: 2, Series: 2 * statusCount, Written: 2 * statusCount, Candidates: 4));

        var history = await store.GetHistoryAsync(DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        history.Should().HaveCount(2 * statusCount, "every status is written, zeros included");
        history.Single(point => point.PlatformId == "EUW1" && point.Status == "Queued").Count.Should().Be(2);
        history.Single(point => point.PlatformId == "EUW1" && point.Status == "Validated").Count.Should().Be(1);
        history.Single(point => point.PlatformId == "KR" && point.Status == "Scored").Count.Should().Be(1);
        history.Single(point => point.PlatformId == "KR" && point.Status == "New").Count.Should()
            .Be(0, "a status with no rows is recorded as a measured zero, not omitted");
    }

    [Fact]
    public async Task RunCoreAsync_WritesNothingRatherThanAnEmptyReading_WhenThereAreNoCandidates()
    {
        await postgres.ResetDatabaseAsync();
        await mongo.ResetAsync();

        using var context = BuildContext();
        var store = new CandidateStockSnapshotStore(context);
        var summary = await BuildProcess(store).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<CandidateStockSnapshotSummary>()
            .Which.Series.Should().Be(0, "no platform holds candidates, so none has anything to report");
        (await store.GetHistoryAsync(DateTime.UtcNow.AddDays(-1), CancellationToken.None)).Should().BeEmpty();
    }

    private CandidateStockSnapshotProcess BuildProcess(ICandidateStockSnapshotStore store) => new(
        NullLogger<CandidateStockSnapshotProcess>.Instance,
        new TestDbContextFactory(postgres),
        store,
        TimeProvider.System);

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            CandidateStockSnapshotsCollection = MongoFixture.CandidateStockSnapshotsCollection,
            Enabled = true
        }));

    private static MainCandidate Candidate(string puuid, int championId, string platformId, MainCandidateStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            Status = status,
            DiscoveredAtUtc = DateTime.UtcNow.AddDays(-1),
            LastPlayTimeUtc = DateTime.UtcNow.AddDays(-1)
        };
}
