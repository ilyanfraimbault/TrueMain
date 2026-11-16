using AwesomeAssertions;
using Data.Logging;
using Data.Ops.Mongo;

namespace TrueMain.UnitTests;

/// <summary>
/// The liveness numbers that must stay ordered (#1229): a running run beats every
/// <see cref="ProcessRunStaleness.HeartbeatInterval"/>, and only reads as Abandoned once it has
/// missed several beats — never after one slow write.
/// </summary>
public sealed class ProcessRunHeartbeatCadenceTests
{
    [Fact]
    public void AStaleRun_HasMissedAtLeastThreeBeats()
        => ProcessRunStaleness.Threshold.Should().BeGreaterThanOrEqualTo(ProcessRunStaleness.HeartbeatInterval * 3);

    [Fact]
    public void TheContainerHealthcheck_ToleratesSeveralBeats()
    {
        // backend/Ingestor/Dockerfile judges the heartbeat file by its age against 300 s.
        var dockerfile = File.ReadAllText(Path.Combine(BackendDirectory(), "Ingestor", "Dockerfile"));

        dockerfile.Should().Contain("-lt 300");
        TimeSpan.FromSeconds(300).Should().BeGreaterThanOrEqualTo(ProcessRunStaleness.HeartbeatInterval * 3);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    [InlineData("abcdef", "abc")]
    public void StoredText_CutsOnlyWhatIsTooLong(string? value, string? expected)
        => StoredText.Truncate(value, 3).Should().Be(expected);

    private static string BackendDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        // <repo>/backend/tests/TrueMain.UnitTests/ProcessRunHeartbeatCadenceTests.cs
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));
}
