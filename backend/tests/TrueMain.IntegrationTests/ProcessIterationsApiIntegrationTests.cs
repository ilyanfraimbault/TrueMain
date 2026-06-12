using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessIterationsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public ProcessIterationsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProcessIterationsAsync_GroupsRunsByIteration_NewestFirstWithOrderedChain()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var olderIteration = Guid.NewGuid();
        var newerIteration = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            // Older iteration: a completed two-process chain.
            db.ProcessRuns.AddRange(
                BuildRun(olderIteration, "Discovery", ProcessRunStatus.Success, now.AddMinutes(-70)),
                BuildRun(olderIteration, "Scoring", ProcessRunStatus.Success, now.AddMinutes(-68)));

            // Newer iteration, still in flight: Discovery succeeded, Scoring failed,
            // MatchIngestion currently Running. Inserted out of order to prove the
            // endpoint orders the chain by StartedAtUtc, not insert order.
            db.ProcessRuns.AddRange(
                BuildRunning(newerIteration, "MatchIngestion", now.AddMinutes(-1)),
                BuildRun(newerIteration, "Discovery", ProcessRunStatus.Success, now.AddMinutes(-10)),
                BuildRun(newerIteration, "Scoring", ProcessRunStatus.Failed, now.AddMinutes(-6)));

            // An un-grouped historical run (no iteration) must NOT appear here.
            db.ProcessRuns.Add(BuildRun(null, "LegacyJob", ProcessRunStatus.Success, now.AddMinutes(-200)));

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/process-iterations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<IterationsContract>();
        payload.Should().NotBeNull();

        // Only the two iteration-grouped passes — the legacy un-grouped run is excluded.
        payload!.Total.Should().Be(2);
        payload.Page.Should().Be(1);
        payload.Iterations.Should().HaveCount(2);

        // Newest iteration first.
        var newest = payload.Iterations[0];
        newest.IterationId.Should().Be(newerIteration);
        newest.IsRunning.Should().BeTrue();
        // Chain in pipeline (StartedAtUtc) order regardless of insert order.
        newest.Runs.Select(run => run.ProcessName)
            .Should().Equal("Discovery", "Scoring", "MatchIngestion");
        newest.Runs.Select(run => run.Status)
            .Should().Equal("Success", "Failed", "Running");

        var oldest = payload.Iterations[1];
        oldest.IterationId.Should().Be(olderIteration);
        oldest.IsRunning.Should().BeFalse();
        oldest.Runs.Select(run => run.ProcessName).Should().Equal("Discovery", "Scoring");
    }

    [Fact]
    public async Task GetProcessIterationsAsync_PagesIterations()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            // Three iterations, one minute apart.
            for (var i = 0; i < 3; i++)
            {
                var id = Guid.NewGuid();
                db.ProcessRuns.Add(
                    BuildRun(id, "Discovery", ProcessRunStatus.Success, now.AddMinutes(-i)));
            }

            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var pageOne = await GetPayloadAsync(client, "/ops/process-iterations?page=1&pageSize=2");
        pageOne.Iterations.Should().HaveCount(2);
        pageOne.Total.Should().Be(3);
        pageOne.PageSize.Should().Be(2);

        var pageTwo = await GetPayloadAsync(client, "/ops/process-iterations?page=2&pageSize=2");
        pageTwo.Iterations.Should().HaveCount(1);
        pageTwo.Total.Should().Be(3);
        pageTwo.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetProcessIterationsAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/process-iterations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private static async Task<IterationsContract> GetPayloadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<IterationsContract>();
        payload.Should().NotBeNull();
        return payload!;
    }

    private static ProcessRun BuildRun(
        Guid? iterationId,
        string processName,
        ProcessRunStatus status,
        DateTime startedAtUtc)
        => new()
        {
            IterationId = iterationId,
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(1),
            DurationMs = (int)TimeSpan.FromMinutes(1).TotalMilliseconds,
            Status = status,
            Host = "test-host"
        };

    private static ProcessRun BuildRunning(Guid? iterationId, string processName, DateTime startedAtUtc)
        => new()
        {
            IterationId = iterationId,
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc,
            DurationMs = 0,
            Status = ProcessRunStatus.Running,
            Host = "test-host"
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class IterationsContract
    {
        public IReadOnlyList<IterationContract> Iterations { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }
    }

    private sealed class IterationContract
    {
        public Guid IterationId { get; init; }

        public DateTime StartedAtUtc { get; init; }

        public DateTime LastActivityAtUtc { get; init; }

        public bool IsRunning { get; init; }

        public IReadOnlyList<IterationRunContract> Runs { get; init; } = [];
    }

    private sealed class IterationRunContract
    {
        public string ProcessName { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;
    }
}
