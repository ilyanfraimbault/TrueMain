using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Data.Aggregation;

namespace TrueMain.UnitTests;

/// <summary>
/// The back-test tools read their test games with hand-written SQL (#1749, #1906). A game for
/// them must be a game for the product too: <see cref="ChampionCohort"/>'s duration floor and
/// Riot's early-surrender flag, or the tools score remakes the site never counts.
/// </summary>
public sealed class BackTestExportCohortTests
{
    [Theory]
    [InlineData("DraftEvaluation")]
    [InlineData("NextItemEvaluation")]
    public void TheExport_ReadsOnlyGames_AsTheCohortDefinesThem(string tool)
    {
        var sql = File.ReadAllText(Path.Combine(ToolsDirectory(), tool, "export.sql"));

        sql.Should().Contain($"\"GameDurationSeconds\" >= {ChampionCohort.MinimumGameDurationSeconds}");
        sql.Should().Contain("not m.\"EndedInEarlySurrender\"");
    }

    private static string ToolsDirectory()
        // <repo>/backend/tests/TrueMain.UnitTests/BackTestExportCohortTests.cs
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "Tools"));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
