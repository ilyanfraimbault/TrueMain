using AwesomeAssertions;
using Data.Entities;

namespace TrueMain.UnitTests;

/// <summary>
/// The two ops lifecycles as explicit tables (#2474): every pair is asserted, so adding a
/// status forces a decision about each of its transitions instead of inheriting one.
/// </summary>
public sealed class OpsLifecycleTransitionsTests
{
    private static readonly HashSet<(SeedRequestStatus From, SeedRequestStatus To)> AllowedSeedTransitions =
    [
        (SeedRequestStatus.Pending, SeedRequestStatus.Resolving),
        (SeedRequestStatus.Resolving, SeedRequestStatus.Pending),
        (SeedRequestStatus.Resolving, SeedRequestStatus.Ingested),
        (SeedRequestStatus.Resolving, SeedRequestStatus.Failed)
    ];

    [Fact]
    public void SeedRequestTransitions_AllowExactlyTheTable()
    {
        foreach (var from in Enum.GetValues<SeedRequestStatus>())
        {
            foreach (var to in Enum.GetValues<SeedRequestStatus>())
            {
                SeedRequestTransitions.IsAllowed(from, to)
                    .Should().Be(AllowedSeedTransitions.Contains((from, to)), $"{from} → {to}");
            }
        }
    }

    [Fact]
    public void SeedRequestTransitions_TerminalWritesOnlyLeaveResolving()
    {
        SeedRequestTransitions.SourcesOf(SeedRequestStatus.Ingested).Should().Equal(SeedRequestStatus.Resolving);
        SeedRequestTransitions.SourcesOf(SeedRequestStatus.Failed).Should().Equal(SeedRequestStatus.Resolving);
        SeedRequestTransitions.SourcesOf(SeedRequestStatus.Pending).Should().Equal(SeedRequestStatus.Resolving);
        SeedRequestTransitions.SourcesOf(SeedRequestStatus.Resolving).Should().Equal(SeedRequestStatus.Pending);
    }

    [Fact]
    public void SeedRequestTransitions_TerminalStatusesHaveNoWayOut()
    {
        foreach (var terminal in Enum.GetValues<SeedRequestStatus>().Where(status => status.IsTerminal()))
        {
            Enum.GetValues<SeedRequestStatus>()
                .Should().NotContain(to => SeedRequestTransitions.IsAllowed(terminal, to));
        }

        Enum.GetValues<SeedRequestStatus>().Where(status => status.IsTerminal())
            .Should().BeEquivalentTo([SeedRequestStatus.Ingested, SeedRequestStatus.Failed]);
    }

    [Theory]
    [InlineData(ProcessRunStatus.Running, false, false, false)]
    [InlineData(ProcessRunStatus.Success, true, false, true)]
    [InlineData(ProcessRunStatus.Failed, true, true, true)]
    [InlineData(ProcessRunStatus.Abandoned, true, true, true)]
    [InlineData(ProcessRunStatus.Skipped, true, false, false)]
    [InlineData(ProcessRunStatus.Cancelled, true, false, true)]
    public void ProcessRunStatus_ClassifiesEveryStatus(
        ProcessRunStatus status, bool terminal, bool failure, bool completed)
    {
        status.IsTerminal().Should().Be(terminal);
        status.CountsAsFailure().Should().Be(failure);
        status.CountsAsCompleted().Should().Be(completed);
        ProcessRunStatusExtensions.FailureStatuses.Contains(status).Should().Be(failure);
        ProcessRunStatusExtensions.CompletedStatuses.Contains(status).Should().Be(completed);
    }

    [Fact]
    public void ProcessRunStatus_OnlyRunningMovesAndOnlyToATerminalStatus()
    {
        foreach (var from in Enum.GetValues<ProcessRunStatus>())
        {
            foreach (var to in Enum.GetValues<ProcessRunStatus>())
            {
                from.CanTransitionTo(to)
                    .Should().Be(from == ProcessRunStatus.Running && to != ProcessRunStatus.Running, $"{from} → {to}");
            }
        }
    }
}
