using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.UnitTests;

/// <summary>
/// The maths a run is judged by on the Processes page (#2298): medians, relative gaps,
/// the thresholds, and the "too short a history says nothing" rule.
/// </summary>
public sealed class ProcessRunBaselineTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(new double[] { 5 }, 5)]
    [InlineData(new double[] { 3, 1, 2 }, 2)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 2.5)]
    public void Median_TakesTheMiddleOrTheMeanOfTheTwoMiddles(double[] values, double expected)
        => ProcessRunBaselineMath.Median(values).Should().Be(expected);

    [Fact]
    public void Median_OfNothingIsNull()
        => ProcessRunBaselineMath.Median([]).Should().BeNull();

    [Theory]
    [InlineData(280, 100, 1.8)]
    [InlineData(50, 100, -0.5)]
    [InlineData(0, 0, 0)]
    public void Deviation_IsTheRelativeGapFromTheMedian(double value, double median, double expected)
        => ProcessRunBaselineMath.Deviation(value, median).Should().BeApproximately(expected, 1e-9);

    [Fact]
    public void Deviation_FromAZeroMedianIsUndefined()
        => ProcessRunBaselineMath.Deviation(3, 0).Should().BeNull();

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.99, false)]
    [InlineData(-0.49, false)]
    [InlineData(-0.5, true)]
    [InlineData(null, false)]
    public void IsFlagged_OnlyPastTheThresholds(double? deviation, bool expected)
        => ProcessRunBaselineMath.IsFlagged(deviation).Should().Be(expected);

    [Fact]
    public void Measure_SaysNothingBelowTheMinimumHistory()
    {
        var stats = ProcessRunBaselineMath.Summarise(
            Enumerable.Range(0, ProcessRunBaselinePolicy.MinSamples - 1)
                .Select(_ => new ProcessRunBaselineSample(1_000, null))
                .ToList());

        ProcessRunBaselineMath.Measure(10_000, null, stats).Should().BeNull();
    }

    [Fact]
    public void Measure_ComparesDurationAndTopLevelNumericCounters()
    {
        var samples = Enumerable.Range(0, 6)
            .Select(i => new ProcessRunBaselineSample(
                1_000 + i,
                $$"""{"inserted":{{100 + i}},"reason":"ok","perPlatform":{"euw1":3} }"""))
            .ToList();
        var stats = ProcessRunBaselineMath.Summarise(samples);

        var baseline = ProcessRunBaselineMath.Measure(3_000, """{"inserted":40,"reason":"ok"}""", stats);

        baseline.Should().NotBeNull();
        baseline!.SampleCount.Should().Be(6);
        baseline.Duration!.Median.Should().Be(1_002.5);
        baseline.Duration.Flagged.Should().BeTrue();
        baseline.Counters.Keys.Should().BeEquivalentTo(["inserted"]);
        baseline.Counters["inserted"].Median.Should().Be(102.5);
        baseline.Counters["inserted"].Flagged.Should().BeTrue();
    }

    [Fact]
    public void Measure_LeavesOutACounterTooFewRunsRecorded()
    {
        var samples = Enumerable.Range(0, 6)
            .Select(i => new ProcessRunBaselineSample(1_000, i < 2 ? """{"rare":5}""" : "not json"))
            .ToList();

        var baseline = ProcessRunBaselineMath.Measure(1_000, """{"rare":50}""", ProcessRunBaselineMath.Summarise(samples));

        baseline!.Counters.Should().BeEmpty();
        baseline.Duration!.Flagged.Should().BeFalse();
    }

    [Fact]
    public async Task AttachAsync_LooksUpEachProcessAndModeOnceAndOnlyBaselinesSuccesses()
    {
        var store = Substitute.For<IProcessRunStore>();
        store.GetRecentSuccessesAsync("Harvest", "FetchLane", ProcessRunBaselinePolicy.SampleSize, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 10).Select(_ => new ProcessRunBaselineSample(1_000, null)).ToList());
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new ProcessRunBaselineService(store, cache);

        var runs = new[]
        {
            Run("Harvest", "FetchLane", ProcessRunStatus.Success, 5_000),
            Run("Harvest", "FetchLane", ProcessRunStatus.Success, 1_000),
            Run("Harvest", "FetchLane", ProcessRunStatus.Failed, 10),
        };

        var first = await service.AttachAsync(runs, Now, CancellationToken.None);
        await service.AttachAsync(runs, Now, CancellationToken.None);

        first[0].Baseline!.Duration!.Flagged.Should().BeTrue();
        first[1].Baseline!.Duration!.Flagged.Should().BeFalse();
        first[2].Baseline.Should().BeNull();
        await store.Received(1).GetRecentSuccessesAsync(
            "Harvest", "FetchLane", ProcessRunBaselinePolicy.SampleSize, Arg.Any<CancellationToken>());
    }

    private static ProcessRunDocument Run(string process, string? mode, ProcessRunStatus status, int durationMs) => new()
    {
        Id = Guid.NewGuid(),
        ProcessName = process,
        JobMode = mode,
        Status = status,
        StartedAtUtc = Now.AddMilliseconds(-durationMs),
        FinishedAtUtc = Now,
        DurationMs = durationMs,
    };
}
