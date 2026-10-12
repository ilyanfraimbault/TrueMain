using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The meter rollups (#1636) against a real Mongo: the <c>$inc</c>-upsert, the unique
/// per-minute key and the window's <c>$group</c> are server-side behaviour a mock could not
/// cover.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MeterRollupStoreIntegrationTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Meter = "TrueMain.Ingestor";
    private const string Wait = "ingestor.riot.ratelimit.wait";
    private static readonly DateTime Minute = new(2026, 10, 6, 12, 34, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync() => await mongo.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task WriteAsync_TwoFlushesOfOneMinute_AddUpIntoOneDocument()
    {
        using var context = BuildContext();
        var store = BuildStore(context);

        await store.WriteAsync([Rollup(Minute, "europe", count: 2, sum: 200, max: 120)], CancellationToken.None);
        await store.WriteAsync([Rollup(Minute, "europe", count: 1, sum: 300, max: 300)], CancellationToken.None);

        var documents = await mongo.GetCollection<MeterRollupDocument>(MongoFixture.MeterRollupsCollection)
            .Find(FilterDefinition<MeterRollupDocument>.Empty).ToListAsync();
        var document = documents.Should().ContainSingle().Subject;
        document.Count.Should().Be(3);
        document.Sum.Should().Be(500);
        document.Max.Should().Be(300);
        document.Tags.Should().Contain("routing_value", "europe");
        document.SeriesKey.Should().Be("endpoint=match-v5.getMatch|routing_value=europe");
    }

    [Fact]
    public async Task GetWindowAsync_SumsEachSeriesOverTheWindow_AndReportsTheOldestMinute()
    {
        using var context = BuildContext();
        var store = BuildStore(context);

        await store.WriteAsync(
            [
                Rollup(Minute.AddHours(-3), "europe", count: 5, sum: 50, max: 20),
                Rollup(Minute, "europe", count: 2, sum: 200, max: 120),
                Rollup(Minute.AddMinutes(1), "europe", count: 1, sum: 10, max: 10),
                Rollup(Minute, "asia", count: 1, sum: 40, max: 40)
            ],
            CancellationToken.None);

        var window = await store.GetWindowAsync(Meter, Minute.AddHours(-1), CancellationToken.None);

        window.OldestRetainedBucketUtc.Should().Be(Minute.AddHours(-3));
        window.Series.Should().HaveCount(2);
        var europe = window.Series.Single(series => series.Tags["routing_value"] == "europe");
        europe.Count.Should().Be(3, "the minute before the window is not counted");
        europe.Sum.Should().Be(210);
        europe.Max.Should().Be(120);
        europe.Kind.Should().Be(MeterRollup.HistogramKind);
        europe.Unit.Should().Be("ms");
    }

    [Fact]
    public async Task GetWindowAsync_WithNothingRecorded_HasNoOldestMinute()
    {
        using var context = BuildContext();

        var window = await BuildStore(context).GetWindowAsync(Meter, Minute, CancellationToken.None);

        window.Series.Should().BeEmpty();
        window.OldestRetainedBucketUtc.Should().BeNull();
    }

    private static MeterRollup Rollup(DateTime bucket, string route, long count, double sum, double max)
        => new(
            bucket,
            Meter,
            Wait,
            MeterRollup.HistogramKind,
            "ms",
            "Time a Riot API call spent waiting for a rate-limit permit.",
            new Dictionary<string, string> { ["routing_value"] = route, ["endpoint"] = "match-v5.getMatch" },
            count,
            sum,
            max,
            bucket.AddSeconds(30));

    private MeterRollupStore BuildStore(MongoLogContext context)
        => new(context, Microsoft.Extensions.Options.Options.Create(Settings(mongo.ConnectionString)));

    private MongoLogContext BuildContext() => new(Microsoft.Extensions.Options.Options.Create(Settings(mongo.ConnectionString)));

    private static MongoLoggingOptions Settings(string connectionString) => new()
    {
        ConnectionString = connectionString,
        Database = MongoFixture.DatabaseName,
        MeterRollupsCollection = MongoFixture.MeterRollupsCollection,
        ProcessName = "Ingestor",
        Enabled = true
    };
}
