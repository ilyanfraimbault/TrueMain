using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises <see cref="MongoStorageStatsReader"/> against a real Mongo container
/// (#1023). It has to be an integration test: every value comes from server-side
/// <c>dbStats</c> / <c>$collStats</c> documents, and the reader reads them by BSON
/// field name. A typo there — or a numeric type the reader fails to widen — would
/// surface as a silent 0 through its defensive fallback, which is exactly the kind of
/// wrong-but-plausible number this whole feature exists to stop showing.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MongoStorageStatsReaderIntegrationTests
{
    private readonly MongoFixture _mongo;

    public MongoStorageStatsReaderIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task GetAsync_ReportsRealPerCollectionAndDatabaseSizes()
    {
        await _mongo.ResetAsync();
        await SeedAsync(MongoFixture.LogsCollection, documents: 500);
        await SeedAsync(MongoFixture.AuditCollection, documents: 50);

        using var context = BuildContext();
        var stats = await new MongoStorageStatsReader(context).GetAsync(CancellationToken.None);

        stats.Should().NotBeNull();

        var logs = stats!.Collections.SingleOrDefault(c => c.TableName == MongoFixture.LogsCollection);
        logs.Should().NotBeNull("the seeded collection must be measured");

        // Exact counts, unlike the Postgres side's planner estimate — a wrong field
        // name here is the difference between "500 documents" and a silent 0.
        logs!.RowEstimate.Should().Be(500);
        logs.TableBytes.Should().BePositive("storageSize must be read, not defaulted");
        logs.IndexBytes.Should().BePositive("every collection has at least the _id index");
        logs.TotalBytes.Should().Be(logs.TableBytes + logs.IndexBytes);

        var audit = stats.Collections.Single(c => c.TableName == MongoFixture.AuditCollection);
        audit.RowEstimate.Should().Be(50);

        // The database total is measured through dbStats, not summed from the
        // collections — but it cannot be smaller than what those collections occupy,
        // and it must not be the defaulted 0.
        stats.DatabaseBytes.Should().BePositive();
        stats.DatabaseBytes.Should().BeGreaterThanOrEqualTo(
            stats.Collections.Sum(collection => collection.TotalBytes));
    }

    [Fact]
    public async Task GetAsync_SkipsViews()
    {
        await _mongo.ResetAsync();
        await SeedAsync(MongoFixture.LogsCollection, documents: 10);

        // A view has no storage of its own, and asking one for storageStats is an
        // error rather than an empty answer — so one appearing in this database must
        // not take the whole snapshot step down with it. Dropped first because
        // ResetAsync only knows about the real collections.
        await _mongo.GetDatabase().DropCollectionAsync("logs_view");
        await _mongo.GetDatabase().CreateViewAsync(
            "logs_view", MongoFixture.LogsCollection, new EmptyPipelineDefinition<BsonDocument>());

        using var context = BuildContext();
        var stats = await new MongoStorageStatsReader(context).GetAsync(CancellationToken.None);

        stats.Should().NotBeNull();
        stats!.Collections.Should().NotContain(collection => collection.TableName == "logs_view");
        stats.Collections.Should().Contain(collection => collection.TableName == MongoFixture.LogsCollection);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenMongoIsNotConfigured()
    {
        // "Not measured" is not "measured as empty": the callers render an engine they
        // could not read differently from one that holds nothing.
        using var context = new MongoLogContext(
            Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions { Enabled = false }));

        var stats = await new MongoStorageStatsReader(context).GetAsync(CancellationToken.None);

        stats.Should().BeNull();
    }

    private async Task SeedAsync(string collection, int documents)
    {
        // Padded so the collection has genuinely non-trivial storage: WiredTiger
        // compresses hard, and a handful of tiny documents can round to nothing.
        var payload = new string('x', 512);
        await _mongo.GetCollection<BsonDocument>(collection).InsertManyAsync(
            Enumerable.Range(0, documents).Select(index => new BsonDocument
            {
                ["seq"] = index,
                ["payload"] = payload
            }));
    }

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = _mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            Enabled = true
        }));
}
