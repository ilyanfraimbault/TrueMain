using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The <c>baseline</c> each run of <c>GET /ops/process-runs</c> carries (#2298): the
/// median of the recent successful runs of the same process and mode, read from Mongo.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunBaselineApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await fixture.ResetDatabaseAsync();
        await mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetProcessRunsAsync_FlagsTheAbnormallySlowRunAgainstItsMedian()
    {
        var now = DateTime.UtcNow;
        var runs = Enumerable.Range(1, 9)
            .Select(i => Run("Harvest", "FetchLane", ProcessRunStatus.Success, now.AddHours(-i), 60_000, """{"inserted":100}"""))
            .Append(Run("Harvest", "FetchLane", ProcessRunStatus.Success, now.AddMinutes(-5), 300_000, """{"inserted":10}"""))
            .Append(Run("Harvest", "FetchLane", ProcessRunStatus.Failed, now.AddMinutes(-2), 1_000, null))
            // Another mode's history never leaks in, and three runs are too few for one.
            .Concat(Enumerable.Range(1, 3)
                .Select(i => Run("Harvest", "Full", ProcessRunStatus.Success, now.AddDays(-i), 1_000, null)))
            .ToList();
        await mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection).InsertManyAsync(runs);

        await using var factory = new ApiWebApplicationFactory(fixture, mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey);

        var response = await client.GetAsync("/ops/process-runs?processName=Harvest");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = document.RootElement.GetProperty("runs").EnumerateArray().ToList();

        page[0].GetProperty("status").GetString().Should().Be("Failed");
        page[0].GetProperty("baseline").ValueKind.Should().Be(JsonValueKind.Null);

        var slow = page[1].GetProperty("baseline");
        slow.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["sampleCount", "duration", "counters"]);
        slow.GetProperty("sampleCount").GetInt32().Should().Be(10);
        slow.GetProperty("duration").GetProperty("median").GetDouble().Should().Be(60_000);
        slow.GetProperty("duration").GetProperty("deviation").GetDouble().Should().Be(4);
        slow.GetProperty("duration").GetProperty("flagged").GetBoolean().Should().BeTrue();
        var inserted = slow.GetProperty("counters").GetProperty("inserted");
        inserted.GetProperty("median").GetDouble().Should().Be(100);
        inserted.GetProperty("flagged").GetBoolean().Should().BeTrue();

        var normal = page[2].GetProperty("baseline");
        normal.GetProperty("duration").GetProperty("flagged").GetBoolean().Should().BeFalse();
        normal.GetProperty("counters").GetProperty("inserted").GetProperty("deviation").GetDouble().Should().Be(0);

        page.Where(run => run.GetProperty("jobMode").GetString() == "Full")
            .Should().OnlyContain(run => run.GetProperty("baseline").ValueKind == JsonValueKind.Null);
    }

    private static ProcessRunDocument Run(
        string process, string mode, ProcessRunStatus status, DateTime startedAtUtc, int durationMs, string? summary) => new()
    {
        Id = Guid.NewGuid(),
        ProcessName = process,
        JobMode = mode,
        StartedAtUtc = startedAtUtc,
        FinishedAtUtc = startedAtUtc.AddMilliseconds(durationMs),
        DurationMs = durationMs,
        Status = status,
        Host = "test-host",
        SummaryJson = summary,
    };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);
}
