using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the day-keyed storage-snapshot upsert against a real Mongo container:
/// the unique index and the upsert filter are server-side behaviour, so a mocked
/// context could not cover either. Focused on what #1023 changed — the engine
/// discriminator, and the fact that two engines genuinely share object names.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DbStorageSnapshotStoreIntegrationTests
{
    private static readonly DateTime Day = new(2026, 8, 5, 9, 30, 0, DateTimeKind.Utc);

    private readonly MongoFixture _mongo;

    public DbStorageSnapshotStoreIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task UpsertDayAsync_KeepsBothEnginesForAnObjectNameTheyShare()
    {
        await _mongo.ResetAsync();
        using var context = BuildContext();
        var store = new DbStorageSnapshotStore(context);

        // process_runs is both a frozen Postgres table and a Mongo collection. Under
        // the pre-#1023 (day, name) key the second write of the day would have
        // overwritten the first, and the panel would have shown one engine's size for
        // the other's object.
        await store.UpsertDayAsync(
            Day, StorageEngines.Postgres, 10_000, [Sample("process_runs", 4_096)], CancellationToken.None);
        await store.UpsertDayAsync(
            Day, StorageEngines.Mongo, 3_000, [Sample("process_runs", 8_192)], CancellationToken.None);

        var history = await store.GetHistoryAsync(Day.AddDays(-1), CancellationToken.None);

        history.Should().HaveCount(2);
        history.Should().ContainSingle(point =>
            point.Engine == StorageEngines.Postgres
            && point.TotalBytes == 4_096
            && point.DatabaseBytes == 10_000);
        history.Should().ContainSingle(point =>
            point.Engine == StorageEngines.Mongo
            && point.TotalBytes == 8_192
            && point.DatabaseBytes == 3_000);
    }

    [Fact]
    public async Task UpsertDayAsync_RefreshesTheDayInPlaceRatherThanAppending()
    {
        await _mongo.ResetAsync();
        using var context = BuildContext();
        var store = new DbStorageSnapshotStore(context);

        // The pipeline runs many times a day; each run refreshes the day's reading.
        await store.UpsertDayAsync(
            Day, StorageEngines.Mongo, 3_000, [Sample("logs", 1_000)], CancellationToken.None);
        await store.UpsertDayAsync(
            Day.AddHours(6), StorageEngines.Mongo, 4_000, [Sample("logs", 2_000)], CancellationToken.None);

        var history = await store.GetHistoryAsync(Day.AddDays(-1), CancellationToken.None);

        history.Should().ContainSingle();
        history[0].TotalBytes.Should().Be(2_000, "the last run of the day wins");
        history[0].DatabaseBytes.Should().Be(4_000);
    }

    [Fact]
    public async Task UpsertDayAsync_StampsPreExistingDocumentsAsPostgres()
    {
        await _mongo.ResetAsync();

        // A document exactly as #925 wrote it: no engine field at all. Left unstamped,
        // the engine-filtered upsert would not match it and would insert a second
        // document for the same day and table — which the read sums, silently doubling
        // the day.
        var raw = _mongo.GetCollection<BsonDocument>(MongoFixture.DbTableSizeSnapshotsCollection);
        await raw.InsertOneAsync(new BsonDocument
        {
            ["snapshotDateUtc"] = Day.Date,
            ["tableName"] = "matches",
            ["rowEstimate"] = 10L,
            ["totalBytes"] = 5_000L,
            ["tableBytes"] = 4_000L,
            ["indexBytes"] = 1_000L,
            ["databaseBytes"] = 50_000L,
            ["capturedAtUtc"] = Day
        });

        using var context = BuildContext();
        var store = new DbStorageSnapshotStore(context);
        await store.UpsertDayAsync(
            Day, StorageEngines.Postgres, 60_000, [Sample("matches", 6_000)], CancellationToken.None);

        var history = await store.GetHistoryAsync(Day.AddDays(-1), CancellationToken.None);

        history.Should().ContainSingle("the legacy document must be updated, not duplicated");
        history[0].Engine.Should().Be(StorageEngines.Postgres);
        history[0].TotalBytes.Should().Be(6_000);
    }

    [Fact]
    public async Task EnsureIndexes_ReplacesTheLegacyUniqueIndexWithTheEngineAwareOne()
    {
        await _mongo.ResetAsync();
        var collection = _mongo.GetCollection<DbTableSizeSnapshotDocument>(
            MongoFixture.DbTableSizeSnapshotsCollection);

        // Recreate #925's index, then let the first upsert reconcile it. Leaving it in
        // place would reject the second engine's write for a shared name with a
        // duplicate-key error.
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<DbTableSizeSnapshotDocument>(
            Builders<DbTableSizeSnapshotDocument>.IndexKeys
                .Ascending(doc => doc.SnapshotDateUtc)
                .Ascending(doc => doc.TableName),
            new CreateIndexOptions { Name = "ux_date_table", Unique = true }));

        using var context = BuildContext();
        var store = new DbStorageSnapshotStore(context);
        await store.UpsertDayAsync(
            Day, StorageEngines.Postgres, 10_000, [Sample("seed_requests", 1_024)], CancellationToken.None);
        await store.UpsertDayAsync(
            Day, StorageEngines.Mongo, 3_000, [Sample("seed_requests", 2_048)], CancellationToken.None);

        using var cursor = await collection.Indexes.ListAsync();
        var names = (await cursor.ToListAsync())
            .Select(index => index["name"].AsString)
            .ToList();

        names.Should().NotContain("ux_date_table");
        names.Should().Contain("ux_date_engine_table");

        var history = await store.GetHistoryAsync(Day.AddDays(-1), CancellationToken.None);
        history.Should().HaveCount(2);
    }

    private static DbTableSizeSample Sample(string name, long totalBytes)
        => new(name, RowEstimate: 1, TotalBytes: totalBytes, TableBytes: totalBytes, IndexBytes: 0);

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = _mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            DbTableSizeSnapshotsCollection = MongoFixture.DbTableSizeSnapshotsCollection,
            Enabled = true
        }));
}
