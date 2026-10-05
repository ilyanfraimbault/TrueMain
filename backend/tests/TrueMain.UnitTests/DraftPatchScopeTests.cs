using TrueMain.Services.Champions.Draft;

namespace TrueMain.UnitTests;

public class DraftPatchScopeTests
{
    private static DraftLaneReader.MatchupRow Row(string patch, int opponent, int games, int wins)
        => new(1, opponent, patch, games, wins, 0, 0);

    [Fact]
    public void WithoutARequestTheTwoNewestPatchesAreRead()
    {
        var scope = DraftPatchScopeResolver.Resolve(null, ["16.19", "16.18", "16.17"]);

        Assert.Equal(new DraftPatchScope("16.19", "16.18"), scope);
    }

    [Fact]
    public void ARequestedPatchFallsBackToTheNewestOneBeforeIt()
    {
        var scope = DraftPatchScopeResolver.Resolve("16.18", ["16.19", "16.18", "16.16"]);

        Assert.Equal(new DraftPatchScope("16.18", "16.16"), scope);
    }

    [Fact]
    public void NoStoredPatchMeansNoPatchClause()
    {
        Assert.Null(DraftPatchScopeResolver.Resolve(null, []).Window);
    }

    [Fact]
    public void AChampionMeasuredOnTheCurrentPatchIsReadFromItAlone()
    {
        var scope = new DraftPatchScope("16.19", "16.18");
        List<DraftLaneReader.MatchupRow> rows =
        [
            Row("16.19", 10, DraftLaneReader.MinCurrentPatchGames, DraftLaneReader.MinCurrentPatchGames / 2),
            Row("16.18", 10, 5_000, 4_000),
        ];

        var record = DraftLaneReader.BuildRecord(rows, scope);

        Assert.Equal("16.19", record.Patch);
        Assert.Equal(DraftLaneReader.MinCurrentPatchGames, record.Games);
    }

    [Fact]
    public void AThinCurrentPatchAddsThePreviousOne()
    {
        var scope = new DraftPatchScope("16.19", "16.18");
        List<DraftLaneReader.MatchupRow> rows =
        [
            Row("16.19", 10, 20, 10),
            Row("16.19", 11, 20, 10),
            Row("16.18", 10, 1_000, 600),
        ];

        var record = DraftLaneReader.BuildRecord(rows, scope);

        Assert.Equal("16.19+16.18", record.Patch);
        Assert.Equal(1_040, record.Games);
        // 610/1020 into 10, against 620/1040 overall.
        Assert.Equal((610d / 1_020) - (620d / 1_040), record.Versus[10].Delta, 9);
    }
}
