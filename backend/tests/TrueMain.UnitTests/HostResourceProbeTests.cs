using AwesomeAssertions;
using Ingestor.FetchWorkers;

namespace TrueMain.UnitTests;

/// <summary>
/// The probe behind the fetch workers' backpressure (#1459): a host without a resource reports
/// nothing, a resource it has but cannot read reports a failure (#2481, F27).
/// </summary>
public sealed class HostResourceProbeTests
{
    [Fact]
    public void Read_OfAReadableDisk_ReportsItsFreeShareAndNoFailure()
    {
        var reading = new HostResourceProbe().Read(Path.GetTempPath());

        reading.FreeDiskPercent.Should().BeInRange(0, 100);
        reading.Failure.Should().BeNull();
        if (File.Exists("/proc/meminfo"))
        {
            reading.AvailableMemoryPercent.Should().BeInRange(0, 100);
        }
        else
        {
            reading.AvailableMemoryPercent.Should().BeNull("a host without /proc/meminfo has no memory brake");
        }
    }

    [Fact]
    public void Read_OfADiskPathThatCannotBeResolved_ReportsTheFailure()
    {
        var reading = new HostResourceProbe().Read("bad\0path");

        reading.FreeDiskPercent.Should().BeNull();
        reading.Failure.Should().Contain("bad");
    }

    [Theory]
    [InlineData("MemTotal: 1000 kB\nMemAvailable: 250 kB\n", 25d)]
    [InlineData("MemTotal: 1000 kB\n", null)]
    [InlineData("MemTotal: 0 kB\nMemAvailable: 0 kB\n", null)]
    public void ParseAvailableMemoryPercent_ReadsTheAvailableShare(string memInfo, double? expected)
        => HostResourceProbe.ParseAvailableMemoryPercent(memInfo).Should().Be(expected);
}
