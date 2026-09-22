using TrueMain.Services.Champions.Draft;
using static TrueMain.Services.Champions.Draft.LanePriorQueryService;

namespace TrueMain.UnitTests;

public class LanePriorWeightingTests
{
    private static LaneRow Row(int championId, string lane, string patch, int games)
        => new(championId, lane, patch, games);

    private static double Share(IReadOnlyDictionary<int, LanePrior> priors, int championId, string lane)
        => priors[championId].ByLane.GetValueOrDefault(lane, 0d);

    [Fact]
    public void TurnsGamesIntoADistributionOverLanes()
    {
        var priors = BuildPriors([Row(1, "TOP", "16.4", 75), Row(1, "JUNGLE", "16.4", 25)], null, [1]);

        Assert.Equal(0.75, Share(priors, 1, "TOP"), precision: 3);
        Assert.Equal(0.25, Share(priors, 1, "JUNGLE"), precision: 3);
    }

    [Fact]
    public void TheCurrentPatchOutweighsTheOlderOnes()
    {
        // Same volume on both patches, but the champion moved lane. The current
        // patch has to win, or a rework takes months to show.
        var priors = BuildPriors(
            [Row(1, "MIDDLE", "16.5", 100), Row(1, "TOP", "16.4", 100)],
            null,
            [1]);

        Assert.True(Share(priors, 1, "MIDDLE") > Share(priors, 1, "TOP"));
    }

    [Fact]
    public void AnEmptyCurrentPatchFallsBackInsteadOfGoingBlind()
    {
        // Patch day: the fold has not caught up, so the newest patch carries no
        // rows at all. Reading only the current patch would return nothing on
        // the one day lanes actually move.
        var priors = BuildPriors([Row(1, "JUNGLE", "16.4", 400)], null, [1]);

        Assert.Equal(1.0, Share(priors, 1, "JUNGLE"), precision: 3);
        Assert.Equal(400, priors[1].Games);
    }

    [Fact]
    public void AChampionWithNoRowsReadsAsUnseenRatherThanAsZeroEverywhere()
    {
        var priors = BuildPriors([], null, [42]);

        Assert.Empty(priors[42].ByLane);
        Assert.Equal(0, priors[42].Games);
    }

    [Fact]
    public void OrdersPatchesNumericallySoSixteenTenIsNewerThanSixteenFour()
    {
        // Ordinal string ordering reads "16.10" as older than "16.4"; the
        // weighting would then be upside down for two weeks after every x.10.
        var priors = BuildPriors(
            [Row(1, "MIDDLE", "16.10", 100), Row(1, "TOP", "16.4", 100)],
            null,
            [1]);

        Assert.True(
            Share(priors, 1, "MIDDLE") > Share(priors, 1, "TOP"),
            "16.10 must weigh more than 16.4");
    }

    [Fact]
    public void ARequestedPatchExcludesAnythingNewerThanIt()
    {
        var priors = BuildPriors(
            [Row(1, "MIDDLE", "16.5", 1000), Row(1, "TOP", "16.4", 100)],
            "16.4",
            [1]);

        Assert.Equal(1.0, Share(priors, 1, "TOP"), precision: 3);
        Assert.Equal(0d, Share(priors, 1, "MIDDLE"));
    }

    [Fact]
    public void PatchesBeyondTheWindowStopCounting()
    {
        // Only four patches are read. A champion that has not been played since
        // must read as unseen rather than as its year-old lane.
        var rows = new List<LaneRow>
        {
            Row(1, "MIDDLE", "16.8", 10),
            Row(1, "MIDDLE", "16.7", 10),
            Row(1, "MIDDLE", "16.6", 10),
            Row(1, "MIDDLE", "16.5", 10),
            Row(1, "TOP", "15.1", 9999),
        };

        var priors = BuildPriors(rows, null, [1]);

        Assert.Equal(1.0, Share(priors, 1, "MIDDLE"), precision: 3);
        Assert.Equal(0d, Share(priors, 1, "TOP"));
    }

    [Fact]
    public void GamesCountEveryPatchEvenWhenTheWeightingIgnoresSome()
    {
        // `Games` says how much evidence exists at all, which is what the caller
        // uses to tell a thin prior from a confident one. Weighting decides the
        // shape; it must not silently shrink the reported sample to nothing.
        var priors = BuildPriors([Row(1, "TOP", "15.1", 500)], null, [1]);

        Assert.Equal(500, priors[1].Games);
    }
}
