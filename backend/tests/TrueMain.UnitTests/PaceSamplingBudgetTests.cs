using AwesomeAssertions;
using Data.Ops.Mongo;
using Ingestor.Options;
using Ingestor.Processes.Components.PaceSampling;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

public sealed class PaceSamplingBudgetTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadAsync_ShrinksThePerRunCapToWhatTheDayLeft()
    {
        var runs = new FakeProcessRunStore();
        runs.Runs.Add(Run(NowUtc.AddDays(-1), """{"riotCalls":500}"""));
        runs.Runs.Add(Run(NowUtc.AddHours(-3), """{"riotCalls":60}"""));
        runs.Runs.Add(Run(NowUtc.AddHours(-1), """{"skipped":true}"""));

        var budget = await PaceSamplingBudget.ReadAsync(
            runs, "PaceSampling", new PaceSamplingOptions { MaxRequestsPerRun = 50, MaxRequestsPerDay = 100 }, NowUtc, CancellationToken.None);

        budget.SpentToday.Should().Be(60);
        budget.CanSpend(40).Should().BeTrue();
        budget.CanSpend(41).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_KeepsThePerRunCap_WithoutADailyCeiling()
    {
        var budget = await PaceSamplingBudget.ReadAsync(
            new FakeProcessRunStore(), "PaceSampling", new PaceSamplingOptions { MaxRequestsPerRun = 5 }, NowUtc, CancellationToken.None);

        budget.CanSpend(5).Should().BeTrue();
        budget.Charge();
        budget.CanSpend(5).Should().BeFalse();
        budget.Spent.Should().Be(1);
    }

    [Theory]
    [InlineData("""{"riotCalls":12}""", 12)]
    [InlineData("""{"riotCalls":-3}""", 0)]
    [InlineData("""{"reason":"x"}""", 0)]
    [InlineData("not json", 0)]
    public void ReadRiotCalls_ReadsTheCounterOrNothing(string json, int expected)
        => PaceSamplingBudget.ReadRiotCalls(json).Should().Be(expected);

    private static ProcessRunDocument Run(DateTime startedAtUtc, string summaryJson) => new()
    {
        ProcessName = "PaceSampling",
        StartedAtUtc = startedAtUtc,
        SummaryJson = summaryJson,
    };
}
