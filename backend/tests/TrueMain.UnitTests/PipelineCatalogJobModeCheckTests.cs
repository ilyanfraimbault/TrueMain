using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Data.Pipeline;

namespace TrueMain.UnitTests;

/// <summary>
/// The deploy preflight <c>.github/scripts/check-job-mode.sh</c> cannot read the catalog, so it
/// keeps the composite modes that run the aggregate lane itself (#1493). This pins that list to
/// <see cref="PipelineCatalog.ModesRunning"/>: a new composite mode fails here, not on a deploy.
/// </summary>
public sealed partial class PipelineCatalogJobModeCheckTests
{
    [Fact]
    public void DeployCheck_RejectsExactlyTheModesThatRunTheAggregateLane()
    {
        var script = File.ReadAllText(ScriptPath());
        var match = AggregateLaneModesLine().Match(script);

        match.Success.Should().BeTrue("the script declares aggregate_lane_modes on one line");
        match.Groups["modes"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().BeEquivalentTo(PipelineCatalog.ModesRunning(PipelineLane.Aggregate).Select(mode => mode.ToString()));
    }

    private static string ScriptPath()
    {
        // <repo>/backend/tests/TrueMain.UnitTests/PipelineCatalogJobModeCheckTests.cs
        var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return Path.Combine(repo, ".github", "scripts", "check-job-mode.sh");
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    [GeneratedRegex("""^aggregate_lane_modes="(?<modes>[^"]*)"$""", RegexOptions.Multiline)]
    private static partial Regex AggregateLaneModesLine();
}
