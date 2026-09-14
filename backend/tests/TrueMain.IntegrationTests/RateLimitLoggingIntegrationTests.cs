using System.Net;
using AwesomeAssertions;
using Data.Logging;
using Data.Logging.Mongo;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// A real rejection travels the whole path to the ops logs (#1555): the limiter's
/// <c>OnRejected</c>, the rejection recorder, the Mongo sink.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RateLimitLoggingIntegrationTests
{
    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public RateLimitLoggingIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
    {
        _postgres = postgres;
        _mongo = mongo;
    }

    [Fact]
    public async Task RejectedRequest_IsPersistedAsARateLimitRejectedRow()
    {
        await _mongo.ResetAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(
            _postgres,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", _mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:LogsCollection", MongoFixture.LogsCollection),
                new KeyValuePair<string, string?>("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "Warning"),
                new KeyValuePair<string, string?>("MongoLogging:FlushInterval", "00:00:00.100"),
                new KeyValuePair<string, string?>("RateLimit:PermitLimit", "1"),
                new KeyValuePair<string, string?>("RateLimit:QueueLimit", "0")
            ]);
        using var client = factory.CreateClient();

        // No endpoint answers this path: the first call is a 404 that spends the only
        // permit of the window, the second is rejected by the limiter.
        (await client.GetAsync("/rate-limit-probe")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/rate-limit-probe")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);
        var rejections = Builders<MongoLogDocument>.Filter.Eq(doc => doc.EventType, nameof(OpsEvents.RateLimitRejected));
        await AsyncWait.UntilAsync(
            async () => await collection.CountDocumentsAsync(rejections) == 1,
            "the rejection to reach the logs collection");

        var row = await collection.Find(rejections).SingleAsync();
        row.Level.Should().Be("Warning");
        row.ProcessName.Should().Be("Api");
        row.RequestMethod.Should().Be("GET");
        row.RequestPath.Should().Be("/rate-limit-probe");
        row.StatusCode.Should().Be(429);
    }
}
