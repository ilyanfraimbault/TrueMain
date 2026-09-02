using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the hour-keyed candidate-stock upsert (#1403) against a real Mongo
/// container: the unique <c>(hour, platform, status)</c> index and the upsert filter are
/// server-side behaviour, so a mocked context could not cover either — and the index is
/// what stops the two ingestor lanes (#1362) from splitting one hour into duplicate
/// documents the read side would then sum.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CandidateStockSnapshotStoreIntegrationTests(MongoFixture mongo)
{
    private static readonly DateTime Instant = new(2026, 8, 5, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpsertHourAsync_RefreshesTheHourInPlaceRatherThanAppending()
    {
        await mongo.ResetAsync();
        using var context = BuildContext();
        var store = new CandidateStockSnapshotStore(context);

        // The pipeline runs several times an hour; each run refreshes the reading. Two
        // documents for one hour would be summed by the read side into a queue twice
        // the size of the one that exists.
        await store.UpsertHourAsync(Instant, [Sample("EUW1", "Queued", 300)], CancellationToken.None);
        await store.UpsertHourAsync(Instant.AddMinutes(20), [Sample("EUW1", "Queued", 310)], CancellationToken.None);

        var history = await store.GetHistoryAsync(Instant.AddDays(-1), CancellationToken.None);

        history.Should().ContainSingle();
        history[0].Count.Should().Be(310, "the last run of the hour wins");
        history[0].SnapshotHourUtc.Should().Be(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpsertHourAsync_KeepsPlatformsAndStatusesApartWithinOneHour()
    {
        await mongo.ResetAsync();
        using var context = BuildContext();
        var store = new CandidateStockSnapshotStore(context);

        await store.UpsertHourAsync(
            Instant,
            [
                Sample("EUW1", "Queued", 300),
                Sample("EUW1", "Scored", 120),
                Sample("KR", "Queued", 40)
            ],
            CancellationToken.None);

        var history = await store.GetHistoryAsync(Instant.AddDays(-1), CancellationToken.None);

        history.Should().HaveCount(3);
        history.Should().ContainSingle(point =>
            point.PlatformId == "KR" && point.Status == "Queued" && point.Count == 40);
    }

    [Fact]
    public async Task GetHistoryAsync_ExcludesHoursBeforeTheWindow()
    {
        await mongo.ResetAsync();
        using var context = BuildContext();
        var store = new CandidateStockSnapshotStore(context);

        await store.UpsertHourAsync(
            Instant.AddDays(-10), [Sample("EUW1", "Queued", 1)], CancellationToken.None);
        await store.UpsertHourAsync(Instant, [Sample("EUW1", "Queued", 2)], CancellationToken.None);

        var history = await store.GetHistoryAsync(Instant.AddDays(-1), CancellationToken.None);

        history.Should().ContainSingle().Which.Count.Should().Be(2);
    }

    private static CandidateStockSample Sample(string platform, string status, long count)
        => new(platform, status, count);

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            CandidateStockSnapshotsCollection = MongoFixture.CandidateStockSnapshotsCollection,
            Enabled = true
        }));
}
