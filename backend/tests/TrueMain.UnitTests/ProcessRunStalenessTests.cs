using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

public sealed class ProcessRunStalenessTests
{
    // `now` is injected, so these are fully deterministic (no wall-clock).
    private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Running_with_no_heartbeat_maps_to_Abandoned()
    {
        // A legacy row (pre-heartbeat) or one that never beat reads as Abandoned.
        ProcessRunStaleness.EffectiveStatus(ProcessRunStatus.Running, null, Now)
            .Should().Be(ProcessRunStatus.Abandoned);
    }

    [Fact]
    public void Running_with_stale_heartbeat_maps_to_Abandoned()
    {
        var stale = Now - ProcessRunStaleness.Threshold - TimeSpan.FromSeconds(1);
        ProcessRunStaleness.EffectiveStatus(ProcessRunStatus.Running, stale, Now)
            .Should().Be(ProcessRunStatus.Abandoned);
    }

    [Fact]
    public void Running_with_fresh_heartbeat_stays_Running()
    {
        var fresh = Now - TimeSpan.FromSeconds(30);
        ProcessRunStaleness.EffectiveStatus(ProcessRunStatus.Running, fresh, Now)
            .Should().Be(ProcessRunStatus.Running);
    }

    [Fact]
    public void Running_with_heartbeat_exactly_at_the_threshold_stays_Running()
    {
        // The staleness check is strict (`< now - Threshold`), so a beat landing
        // exactly on the boundary still counts as fresh. Guards the comparison
        // sense and the threshold value against regressions.
        var boundary = Now - ProcessRunStaleness.Threshold;
        ProcessRunStaleness.EffectiveStatus(ProcessRunStatus.Running, boundary, Now)
            .Should().Be(ProcessRunStatus.Running);
    }

    [Theory]
    [InlineData(ProcessRunStatus.Success)]
    [InlineData(ProcessRunStatus.Failed)]
    [InlineData(ProcessRunStatus.Abandoned)]
    public void Non_running_statuses_pass_through_unchanged(ProcessRunStatus status)
    {
        // A terminal status is returned verbatim regardless of heartbeat age.
        ProcessRunStaleness.EffectiveStatus(status, null, Now).Should().Be(status);
    }
}
