using AwesomeAssertions;
using Data.Ops.Mongo;
using Ingestor.Options;
using Ingestor.Processes.Summaries;

namespace TrueMain.UnitTests;

/// <summary>
/// The three bounds #1474 puts on the ladder sync — a run cadence, a daily request ceiling and
/// an apex-refresh cadence — each measured against the process's own run history rather than
/// against the loop that happens to invoke it.
/// </summary>
public sealed class LadderSyncCadenceTests
{
    // The harness pins the process clock; every run below is placed relative to it.
    private static readonly DateTime NowUtc = LadderSyncProcessTests.NowUtc;

    [Fact]
    public async Task RunCoreAsync_Skips_WhenLastRunWithinMinRunInterval()
    {
        var client = new LadderSyncProcessTests.RecordingPlatformClient();
        var harness = new LadderSyncProcessTests.Harness(client) { LastCompletedRunUtc = NowUtc.AddHours(-1) };

        var summary = await harness.RunRawAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Master", "Diamond"],
            MaxRequestsPerRun = 5,
            MinRunInterval = TimeSpan.FromHours(4)
        });

        summary.Should().BeOfType<SkippedSummary>().Which.Skipped.Should().BeTrue();
        client.ApexCalls.Should().BeEmpty();
        client.PagedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCoreAsync_Runs_WhenLastRunOlderThanMinRunInterval()
    {
        var client = new LadderSyncProcessTests.RecordingPlatformClient { PagesPerDivision = 1 };
        var harness = new LadderSyncProcessTests.Harness(client) { LastCompletedRunUtc = NowUtc.AddHours(-5) };

        var summary = await harness.RunAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Master", "Diamond"],
            MaxRequestsPerRun = 2,
            MinRunInterval = TimeSpan.FromHours(4)
        });

        summary.ApexCalls.Should().Be(1);
        summary.PagedCalls.Should().Be(2);
    }

    [Fact]
    public async Task RunCoreAsync_SpendsOnlyWhatIsLeftOfTheDay()
    {
        // Two earlier runs today already spent 7 of the 10 daily calls; the per-run cap of 5
        // must shrink to the 3 that remain. Yesterday's run is outside the day and free.
        var client = new LadderSyncProcessTests.RecordingPlatformClient();
        var harness = new LadderSyncProcessTests.Harness(client);
        harness.EarlierRuns.Add(Run(NowUtc.AddDays(-1), pagedCalls: 5, apexCalls: 3));
        harness.EarlierRuns.Add(Run(NowUtc.AddHours(-6), pagedCalls: 4, apexCalls: 3));
        harness.EarlierRuns.Add(Run(NowUtc.AddHours(-2), pagedCalls: 3, apexCalls: 0));

        var summary = await harness.RunAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Diamond"],
            MaxRequestsPerRun = 5,
            MaxRequestsPerDay = 10
        });

        summary.PagedCalls.Should().Be(3);
    }

    [Fact]
    public async Task RunCoreAsync_Skips_WhenDailyBudgetIsSpentAndApexIsNotDue()
    {
        var client = new LadderSyncProcessTests.RecordingPlatformClient();
        var harness = new LadderSyncProcessTests.Harness(client);
        harness.EarlierRuns.Add(Run(NowUtc.AddHours(-1), pagedCalls: 10, apexCalls: 1));

        var summary = await harness.RunRawAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Master", "Diamond"],
            MaxRequestsPerRun = 5,
            MaxRequestsPerDay = 10,
            ApexRefreshInterval = TimeSpan.FromHours(4)
        });

        summary.Should().BeOfType<SkippedSummary>().Which.Skipped.Should().BeTrue();
        client.ApexCalls.Should().BeEmpty();
        client.PagedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCoreAsync_RefreshesApexOnly_WhenDailyBudgetIsSpentButApexIsDue()
    {
        var client = new LadderSyncProcessTests.RecordingPlatformClient();
        var harness = new LadderSyncProcessTests.Harness(client);
        harness.EarlierRuns.Add(Run(NowUtc.AddHours(-5), pagedCalls: 10, apexCalls: 1));

        var summary = await harness.RunAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Master", "Diamond"],
            MaxRequestsPerRun = 5,
            MaxRequestsPerDay = 10,
            ApexRefreshInterval = TimeSpan.FromHours(4)
        });

        summary.ApexCalls.Should().Be(1);
        summary.PagedCalls.Should().Be(0);
    }

    [Fact]
    public async Task RunCoreAsync_SweepsWithoutApex_WhenApexWasRefreshedWithinItsInterval()
    {
        var client = new LadderSyncProcessTests.RecordingPlatformClient();
        var harness = new LadderSyncProcessTests.Harness(client);
        harness.EarlierRuns.Add(Run(NowUtc.AddHours(-1), pagedCalls: 2, apexCalls: 1));

        var summary = await harness.RunAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Master", "Diamond"],
            MaxRequestsPerRun = 2,
            ApexRefreshInterval = TimeSpan.FromHours(4)
        });

        summary.ApexCalls.Should().Be(0);
        summary.PagedCalls.Should().Be(2);
        client.ApexCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCoreAsync_IgnoresRunsWithoutASummaryOrWithAForeignShape()
    {
        // A skip and a failed run carry no counters; neither may be charged to the day.
        var client = new LadderSyncProcessTests.RecordingPlatformClient();
        var harness = new LadderSyncProcessTests.Harness(client);
        harness.EarlierRuns.Add(new ProcessRunSummarySample("LadderSync", NowUtc.AddHours(-3), null));
        harness.EarlierRuns.Add(new ProcessRunSummarySample("LadderSync", NowUtc.AddHours(-2), """{"reason":"Within MinRunInterval","skipped":true}"""));
        harness.EarlierRuns.Add(new ProcessRunSummarySample("LadderSync", NowUtc.AddHours(-1), "not json"));

        var summary = await harness.RunAsync(new LadderSyncOptions
        {
            Platforms = ["KR"],
            TierScope = ["Diamond"],
            MaxRequestsPerRun = 5,
            MaxRequestsPerDay = 5
        });

        summary.PagedCalls.Should().Be(5);
    }

    private static ProcessRunSummarySample Run(DateTime startedAtUtc, int pagedCalls, int apexCalls)
        => new(
            "LadderSync",
            startedAtUtc,
            $$"""{"apexCalls":{{apexCalls}},"pagedCalls":{{pagedCalls}},"failedCalls":0,"entriesFetched":0}""");
}
