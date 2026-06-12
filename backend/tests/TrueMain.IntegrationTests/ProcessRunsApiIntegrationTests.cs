using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public ProcessRunsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldReturnRunsNewestFirstWithRollup()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedProcessRunsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/process-runs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["runs", "rollup", "total", "page", "pageSize"]);

        var firstRun = document.RootElement.GetProperty("runs")[0];
        firstRun.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "id", "processName", "startedAtUtc", "finishedAtUtc",
                "durationMs", "status", "error", "host", "summary"
            ]);

        var firstRollup = document.RootElement.GetProperty("rollup")[0];
        firstRollup.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "processName", "lastStatus", "lastRunAtUtc", "lastSuccessAtUtc",
                "failureCountInWindow", "runCountInWindow", "failureRateInWindow"
            ]);

        var payload = await response.Content.ReadFromJsonAsync<ProcessRunsTestContract>();
        payload.Should().NotBeNull();

        // No `since`: the runs list has NO default time lower bound, so every
        // seeded run is returned — including the 10-day-old and 30-day-old rows
        // that predate the rollup's 7-day failure window.
        payload!.Runs.Should().HaveCount(6);
        // Newest started run first.
        payload.Runs[0].ProcessName.Should().Be("MatchIngestion");
        payload.Runs[0].Status.Should().Be("Failed");
        payload.Runs.Should().BeInDescendingOrder(run => run.StartedAtUtc);

        // No paging params: page 1 at the default page size, with the pre-paging
        // total so the panel can render a pager.
        payload.Total.Should().Be(6);
        payload.Page.Should().Be(1);
        payload.PageSize.Should().Be(100);

        // No `since` => the failure window is UNBOUNDED, so FailureCountInWindow is
        // a true all-time total. MatchIngestion has TWO failures (the recent one
        // and the 30-day-old one), both counted now. The latest run is Failed and
        // there is an earlier success. Three MatchIngestion runs total (1 success,
        // 2 failures), so the all-time failure rate is 2/3.
        var ingestion = payload.Rollup.Single(row => row.ProcessName == "MatchIngestion");
        ingestion.LastStatus.Should().Be("Failed");
        ingestion.FailureCountInWindow.Should().Be(2);
        ingestion.RunCountInWindow.Should().Be(3);
        ingestion.FailureRateInWindow.Should().BeApproximately(2d / 3d, 0.0001);
        ingestion.LastSuccessAtUtc.Should().NotBeNull();

        var mainAnalysis = payload.Rollup.Single(row => row.ProcessName == "MainAnalysis");
        mainAnalysis.LastStatus.Should().Be("Success");
        mainAnalysis.FailureCountInWindow.Should().Be(0);
        mainAnalysis.RunCountInWindow.Should().Be(1);
        mainAnalysis.FailureRateInWindow.Should().Be(0);

        // Discovery's only run is 10 days old: with the unbounded all-time window
        // it is counted (one success, zero failures) and still anchors the
        // rollup's last-run/last-success.
        var discovery = payload.Rollup.Single(row => row.ProcessName == "Discovery");
        discovery.LastStatus.Should().Be("Success");
        discovery.FailureCountInWindow.Should().Be(0);
        discovery.RunCountInWindow.Should().Be(1);
        discovery.LastSuccessAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProcessRunsAsync_WithSince_ShouldNarrowFailureWindowConsistently()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedProcessRunsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // An explicit 7-day `since` narrows BOTH the runs list and the rollup's
        // in-window counts. MatchIngestion's 30-day-old failure now falls outside
        // the window, so only the recent failure is counted (1 of 2 in-window
        // runs failed -> 0.5).
        var since = DateTime.UtcNow.AddDays(-7).ToString("O");
        var payload = await GetPayloadAsync(client, $"/ops/process-runs?since={Uri.EscapeDataString(since)}");

        // The 30-day-old MatchIngestion failure and the 10-day-old Discovery run
        // are now excluded from the runs list too. Assert the stale failure by its
        // age (well outside the window) rather than its seeded error text — a seed
        // rename could otherwise make this NotContain vacuously pass.
        payload.Runs.Should().NotContain(run =>
            run.ProcessName == "MatchIngestion"
            && run.StartedAtUtc < DateTime.UtcNow.AddDays(-14));
        payload.Runs.Should().NotContain(run => run.ProcessName == "Discovery");

        var ingestion = payload.Rollup.Single(row => row.ProcessName == "MatchIngestion");
        ingestion.FailureCountInWindow.Should().Be(1);
        ingestion.RunCountInWindow.Should().Be(2);
        ingestion.FailureRateInWindow.Should().BeApproximately(0.5d, 0.0001);
        // Last-run / last-success stay unbounded regardless of the window.
        ingestion.LastStatus.Should().Be("Failed");
        ingestion.LastSuccessAtUtc.Should().NotBeNull();

        // Discovery's only run predates the window: it has no in-window runs, so
        // the rate is 0 (no division-by-zero), but its unbounded last-run survives.
        var discovery = payload.Rollup.Single(row => row.ProcessName == "Discovery");
        discovery.RunCountInWindow.Should().Be(0);
        discovery.FailureCountInWindow.Should().Be(0);
        discovery.FailureRateInWindow.Should().Be(0);
        discovery.LastSuccessAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProcessRunsAsync_WithoutSince_ShouldReturnOldRunsCappedByLimitNewestFirst()
    {
        await _fixture.ResetDatabaseAsync();

        // Seed ONLY runs older than a week. The old behaviour (a default now-7d
        // lower bound on the runs list) would return an empty list here — the bug.
        // With the fix the runs list is unbounded by default, and so is the
        // rollup's failure window.
        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            db.ProcessRuns.AddRange(
                BuildRun("LegacyJob", ProcessRunStatus.Success, now.AddDays(-8), error: null, summary: null),
                BuildRun("LegacyJob", ProcessRunStatus.Failed, now.AddDays(-9), error: "old failure", summary: null),
                BuildRun("LegacyJob", ProcessRunStatus.Success, now.AddDays(-30), error: null, summary: null));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // No `since`, limit=2: the two NEWEST runs come back even though all rows
        // are older than 7 days.
        var response = await client.GetAsync("/ops/process-runs?limit=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ProcessRunsTestContract>();
        payload.Should().NotBeNull();

        payload!.Runs.Should().HaveCount(2);
        payload.Runs.Should().BeInDescendingOrder(run => run.StartedAtUtc);
        // The -8d and -9d runs are the two newest; the -30d run is dropped by the limit.
        payload.Runs[0].StartedAtUtc.Should().BeCloseTo(now.AddDays(-8), TimeSpan.FromSeconds(5));
        payload.Runs[1].StartedAtUtc.Should().BeCloseTo(now.AddDays(-9), TimeSpan.FromSeconds(5));

        // Legacy `limit` (no `pageSize`) acts as the page size on page 1, and the
        // total still counts every matching run beyond the page.
        payload.Total.Should().Be(3);
        payload.Page.Should().Be(1);
        payload.PageSize.Should().Be(2);

        // No `since` => unbounded failure window: the -9d failure IS counted now
        // (a true all-time total), across all three runs (1 failure / 3 runs).
        var legacy = payload.Rollup.Single(row => row.ProcessName == "LegacyJob");
        legacy.FailureCountInWindow.Should().Be(1);
        legacy.RunCountInWindow.Should().Be(3);
        legacy.FailureRateInWindow.Should().BeApproximately(1d / 3d, 0.0001);
        legacy.LastStatus.Should().Be("Success");
        legacy.LastSuccessAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProcessRunsAsync_WithPaging_ShouldReturnRequestedSliceWithTotals()
    {
        await _fixture.ResetDatabaseAsync();

        // Five PagedJob runs, one hour apart, newest-first order:
        // -1h Failed, -2h Success, -3h Failed, -4h Success, -5h Success.
        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            db.ProcessRuns.AddRange(
                BuildRun("PagedJob", ProcessRunStatus.Failed, now.AddHours(-1), error: "newest failure", summary: null),
                BuildRun("PagedJob", ProcessRunStatus.Success, now.AddHours(-2), error: null, summary: null),
                BuildRun("PagedJob", ProcessRunStatus.Failed, now.AddHours(-3), error: "older failure", summary: null),
                BuildRun("PagedJob", ProcessRunStatus.Success, now.AddHours(-4), error: null, summary: null),
                BuildRun("PagedJob", ProcessRunStatus.Success, now.AddHours(-5), error: null, summary: null));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Page 1: the two newest runs.
        var pageOne = await GetPayloadAsync(client, "/ops/process-runs?page=1&pageSize=2");
        pageOne.Runs.Should().HaveCount(2);
        pageOne.Runs[0].StartedAtUtc.Should().BeCloseTo(now.AddHours(-1), TimeSpan.FromSeconds(5));
        pageOne.Runs[1].StartedAtUtc.Should().BeCloseTo(now.AddHours(-2), TimeSpan.FromSeconds(5));
        pageOne.Total.Should().Be(5);
        pageOne.Page.Should().Be(1);
        pageOne.PageSize.Should().Be(2);

        // Page 2: the next slice, with the same total.
        var pageTwo = await GetPayloadAsync(client, "/ops/process-runs?page=2&pageSize=2");
        pageTwo.Runs.Should().HaveCount(2);
        pageTwo.Runs[0].StartedAtUtc.Should().BeCloseTo(now.AddHours(-3), TimeSpan.FromSeconds(5));
        pageTwo.Runs[1].StartedAtUtc.Should().BeCloseTo(now.AddHours(-4), TimeSpan.FromSeconds(5));
        pageTwo.Total.Should().Be(5);
        pageTwo.Page.Should().Be(2);

        // The rollup is computed over the FULL filtered set, so it is identical
        // on every page: both in-window failures are counted even though page 2
        // shows only one of them.
        var rollup = pageTwo.Rollup.Single(row => row.ProcessName == "PagedJob");
        rollup.LastStatus.Should().Be("Failed");
        rollup.FailureCountInWindow.Should().Be(2);

        // Last page: a partial slice, not an error.
        var pageThree = await GetPayloadAsync(client, "/ops/process-runs?page=3&pageSize=2");
        pageThree.Runs.Should().HaveCount(1);
        pageThree.Runs[0].StartedAtUtc.Should().BeCloseTo(now.AddHours(-5), TimeSpan.FromSeconds(5));
        pageThree.Total.Should().Be(5);

        // Past the end: empty page, totals intact.
        var pageFour = await GetPayloadAsync(client, "/ops/process-runs?page=4&pageSize=2");
        pageFour.Runs.Should().BeEmpty();
        pageFour.Total.Should().Be(5);

        // When both are sent, `pageSize` supersedes the legacy `limit`.
        var supersede = await GetPayloadAsync(client, "/ops/process-runs?limit=1&pageSize=3");
        supersede.Runs.Should().HaveCount(3);
        supersede.PageSize.Should().Be(3);

        // Filters combine with paging: the failures-only list has its own total
        // and page 2 (size 1) holds the second-newest failure.
        var filtered = await GetPayloadAsync(client, "/ops/process-runs?status=Failed&page=2&pageSize=1");
        filtered.Runs.Should().HaveCount(1);
        filtered.Runs[0].Error.Should().Be("older failure");
        filtered.Total.Should().Be(2);
        filtered.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldSurfaceSummaryJsonAndApplyFilters()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedProcessRunsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Filter to MatchIngestion failures only. Two failures exist (a recent one
        // and a 30-day-old one); with no `since` both come back, newest first.
        var response = await client.GetAsync("/ops/process-runs?processName=MatchIngestion&status=Failed&limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var runs = document.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(2);
        document.RootElement.GetProperty("total").GetInt64().Should().Be(2);

        // Newest first: the recent failure carrying the JSON summary leads.
        var failedRun = runs[0];
        failedRun.GetProperty("processName").GetString().Should().Be("MatchIngestion");
        failedRun.GetProperty("status").GetString().Should().Be("Failed");
        failedRun.GetProperty("error").GetString().Should().Be("boom: ingest failed");

        // Summary is surfaced verbatim as a JSON object, not a string.
        var summary = failedRun.GetProperty("summary");
        summary.ValueKind.Should().Be(JsonValueKind.Object);
        summary.GetProperty("processed").GetInt32().Should().Be(42);
        summary.GetProperty("stage").GetString().Should().Be("fetch");

        // Rollup is scoped to the same process filter.
        var rollup = document.RootElement.GetProperty("rollup");
        rollup.GetArrayLength().Should().Be(1);
        rollup[0].GetProperty("processName").GetString().Should().Be("MatchIngestion");
    }

    [Fact]
    public async Task GetProcessRunsAsync_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/process-runs");
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

    private static async Task<ProcessRunsTestContract> GetPayloadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ProcessRunsTestContract>();
        payload.Should().NotBeNull();
        return payload!;
    }

    private async Task SeedProcessRunsAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        db.ProcessRuns.AddRange(
            // MatchIngestion: an in-window success then a later in-window failure
            // carrying a JSON summary. The failure is the latest run.
            BuildRun("MatchIngestion", ProcessRunStatus.Success, now.AddHours(-5), error: null, summary: null),
            BuildRun(
                "MatchIngestion",
                ProcessRunStatus.Failed,
                now.AddHours(-1),
                error: "boom: ingest failed",
                summary: "{\"processed\":42,\"stage\":\"fetch\"}"),
            // MatchIngestion: a 30-day-old failure — present in the (now unbounded)
            // runs list but OUTSIDE the 7-day failure window, so it must NOT be
            // counted in failureCountInWindow.
            BuildRun("MatchIngestion", ProcessRunStatus.Failed, now.AddDays(-30), error: "stale failure", summary: null),
            // MainAnalysis: single in-window success.
            BuildRun("MainAnalysis", ProcessRunStatus.Success, now.AddHours(-2), error: null, summary: null),
            // Discovery: a 10-day-old success — outside the 7-day failure window,
            // but with no default lower bound it now appears in the runs list and
            // still anchors the rollup's last-run/last-success for Discovery.
            BuildRun("Discovery", ProcessRunStatus.Success, now.AddDays(-10), error: null, summary: null),
            // Scoring: an in-window success.
            BuildRun("Scoring", ProcessRunStatus.Success, now.AddHours(-3), error: null, summary: null));

        await db.SaveChangesAsync();
    }

    private static ProcessRun BuildRun(
        string processName,
        ProcessRunStatus status,
        DateTime startedAtUtc,
        string? error,
        string? summary)
        => new()
        {
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(2),
            DurationMs = (int)TimeSpan.FromMinutes(2).TotalMilliseconds,
            Status = status,
            Error = error,
            Host = "test-host",
            Summary = summary is null ? null : JsonDocument.Parse(summary)
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class ProcessRunsTestContract
    {
        public IReadOnlyList<ProcessRunTestContract> Runs { get; init; } = [];

        public IReadOnlyList<ProcessRunRollupTestContract> Rollup { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }
    }

    private sealed class ProcessRunTestContract
    {
        public Guid Id { get; init; }

        public string ProcessName { get; init; } = string.Empty;

        public DateTime StartedAtUtc { get; init; }

        public string Status { get; init; } = string.Empty;

        public string? Error { get; init; }
    }

    private sealed class ProcessRunRollupTestContract
    {
        public string ProcessName { get; init; } = string.Empty;

        public string LastStatus { get; init; } = string.Empty;

        public DateTime LastRunAtUtc { get; init; }

        public DateTime? LastSuccessAtUtc { get; init; }

        public int FailureCountInWindow { get; init; }

        public int RunCountInWindow { get; init; }

        public double FailureRateInWindow { get; init; }
    }
}
