using AwesomeAssertions;
using Data.Configuration;
using Data.Ops.Mongo;
using Microsoft.Extensions.Configuration;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.UnitTests;

/// <summary>
/// The deploy markers (#2297) are only worth drawing if a marker means "a new version started
/// here": a restart on the same version must never draw one, two containers rolling the same
/// release at once must draw one, and a first boot on record is a deploy.
/// </summary>
public sealed class DeploymentTimelineTests
{
    private static readonly DateTime Day = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, "1.2.0", true)]
    [InlineData("1.1.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2.0", null, false)]
    [InlineData(null, null, false)]
    [InlineData("1.2.0-rc.1", "1.2.0-RC.1", true)]
    public void AVersionChangeIsADifferentVersionOrTheFirstOneOnRecord(
        string? previous,
        string? current,
        bool expected)
        => ProcessVersions.IsVersionChange(previous, current).Should().Be(expected);

    [Fact]
    public void KeepsEveryChangeWhenEachOneIsANewVersion()
    {
        var changes = new[]
        {
            Deploy("Api", null, "1.1.0", Day.AddHours(1)),
            Deploy("Ingestor", "FetchWorkers", "1.1.0", Day.AddHours(1.1)),
            Deploy("Api", null, "1.2.0", Day.AddHours(5))
        };

        var deploys = DeploymentTimeline.Collapse([], changes);

        deploys.Select(deploy => (deploy.ProcessName, deploy.Version))
            .Should().Equal(("Api", "1.1.0"), ("Ingestor", "1.1.0"), ("Api", "1.2.0"));
    }

    [Fact]
    public void DropsASecondContainerOfOneRoleFlaggingTheSameRelease()
    {
        // Both read 1.1.0 as their predecessor and both flagged themselves.
        var changes = new[]
        {
            Deploy("Api", null, "1.2.0", Day.AddMinutes(1)),
            Deploy("Api", null, "1.2.0", Day.AddMinutes(1.5))
        };

        DeploymentTimeline.Collapse([], changes).Should().ContainSingle()
            .Which.DeployedAtUtc.Should().Be(Day.AddMinutes(1));
    }

    [Fact]
    public void ComparesTheFirstChangeInTheWindowToTheLastOneBeforeIt()
    {
        var before = new[]
        {
            Deploy("Api", null, "1.2.0", Day.AddDays(-2)),
            Deploy("Ingestor", "FetchWorkers", "1.1.0", Day.AddDays(-3))
        };
        var changes = new[]
        {
            Deploy("Api", null, "1.2.0", Day.AddHours(1)),
            Deploy("Ingestor", "FetchWorkers", "1.2.0", Day.AddHours(1))
        };

        DeploymentTimeline.Collapse(before, changes).Should().ContainSingle()
            .Which.ProcessName.Should().Be("Ingestor");
    }

    [Fact]
    public void TreatsEachRoleOfOneProcessAsItsOwnTimeline()
    {
        var changes = new[]
        {
            Deploy("Ingestor", "FetchWorkers", "1.2.0", Day.AddHours(1)),
            Deploy("Ingestor", "AggregateLane", "1.2.0", Day.AddHours(1))
        };

        DeploymentTimeline.Collapse([], changes).Should().HaveCount(2);
    }

    [Fact]
    public void ReturnsTheChangesOldestFirstWhateverOrderTheyArrive()
    {
        var changes = new[]
        {
            Deploy("Api", null, "1.3.0", Day.AddHours(9)),
            Deploy("Api", null, "1.2.0", Day.AddHours(1))
        };

        DeploymentTimeline.Collapse([], changes).Select(deploy => deploy.Version)
            .Should().Equal("1.2.0", "1.3.0");
    }

    [Fact]
    public void KeepsARollbackAsADeploy()
    {
        var changes = new[]
        {
            Deploy("Api", null, "1.3.0", Day.AddHours(1)),
            Deploy("Api", null, "1.2.0", Day.AddHours(2))
        };

        DeploymentTimeline.Collapse([Deploy("Api", null, "1.2.0", Day.AddDays(-1))], changes)
            .Select(deploy => deploy.Version).Should().Equal("1.3.0", "1.2.0");
    }

    [Fact]
    public void RejectsAnInvertedOrOversizedWindow()
    {
        DeploymentsQueryService.Validate(Day, Day.AddDays(-1)).Should().NotBeNull();
        DeploymentsQueryService.Validate(Day.AddDays(-401), Day).Should().NotBeNull();
        DeploymentsQueryService.Validate(Day.AddDays(-30), Day).Should().BeNull();
        DeploymentsQueryService.Validate(null, Day).Should().BeNull();
    }

    [Theory]
    [InlineData("1.25.2", "1.25.2")]
    [InlineData(" 1.26.0-rc.3 ", "1.26.0-rc.3")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void ReadsTheDeployedVersionFromConfigurationOnly(string? configured, string? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(AppVersion.ConfigurationKey, configured)])
            .Build();

        AppVersion.FromConfiguration(configuration).Should().Be(expected);
    }

    private static ProcessDeployment Deploy(string process, string? role, string version, DateTime at)
        => new(process, role, version, null, at);
}
