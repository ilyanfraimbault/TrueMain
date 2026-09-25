using TrueMain.Services.Champions.Draft;

namespace TrueMain.UnitTests;

public class LaneAssignmentSolverTests
{
    private static LanePrior Prior(int championId, params (string Lane, double Share)[] lanes)
        => new()
        {
            ChampionId = championId,
            ByLane = lanes.ToDictionary(l => l.Lane, l => l.Share),
            Games = 500,
        };

    private static Dictionary<int, LanePrior> Priors(params LanePrior[] priors)
        => priors.ToDictionary(p => p.ChampionId);

    /// <summary>A champion that only ever plays one lane.</summary>
    private static LanePrior Dedicated(int championId, string lane) => Prior(championId, (lane, 0.98));

    [Fact]
    public void PlacesEachChampionInItsOwnLane()
    {
        var priors = Priors(
            Dedicated(1, "TOP"),
            Dedicated(2, "JUNGLE"),
            Dedicated(3, "MIDDLE"),
            Dedicated(4, "BOTTOM"),
            Dedicated(5, "UTILITY"));

        var result = LaneAssignmentSolver.Solve([1, 2, 3, 4, 5], priors);

        Assert.Equal("TOP", result.Single(a => a.ChampionId == 1).Position);
        Assert.Equal("UTILITY", result.Single(a => a.ChampionId == 5).Position);
    }

    [Fact]
    public void ResolvesACollisionThatIndependentArgMaxesWouldGetWrong()
    {
        // Both champions play MIDDLE most often, but only one of them ever goes
        // top. Picking each champion's favourite lane independently would put
        // both mid and leave top empty — and would hand the wrong opponent to a
        // top laner asking who they face.
        var priors = Priors(
            Prior(1, ("MIDDLE", 0.60), ("TOP", 0.39)),
            Prior(2, ("MIDDLE", 0.95), ("JUNGLE", 0.04)));

        var result = LaneAssignmentSolver.Solve([1, 2], priors);

        Assert.Equal("TOP", result.Single(a => a.ChampionId == 1).Position);
        Assert.Equal("MIDDLE", result.Single(a => a.ChampionId == 2).Position);
    }

    [Fact]
    public void HandlesAPartialDraftWithoutPaddingTheMissingPicks()
    {
        var result = LaneAssignmentSolver.Solve([1], Priors(Dedicated(1, "JUNGLE")));

        var assignment = Assert.Single(result);
        Assert.Equal("JUNGLE", assignment.Position);
    }

    [Fact]
    public void ReturnsNothingForAnEmptyDraft()
    {
        Assert.Empty(LaneAssignmentSolver.Solve([], Priors()));
    }

    [Fact]
    public void APinnedLaneIsHonouredAndReportedAsCertain()
    {
        var priors = Priors(Dedicated(1, "MIDDLE"), Dedicated(2, "TOP"));

        var result = LaneAssignmentSolver.Solve(
            [1, 2], priors, pinned: new Dictionary<int, string> { [1] = "TOP" });

        var pinned = result.Single(a => a.ChampionId == 1);
        Assert.Equal("TOP", pinned.Position);
        Assert.True(pinned.Pinned);
        Assert.Equal(1d, pinned.Confidence);
    }

    [Fact]
    public void OneCorrectionRearrangesTheLanesAroundIt()
    {
        // The whole point of pinning: correcting the jungler must free the lane
        // it was wrongly holding, so the other champion lands correctly without
        // the user touching it. One gesture, several fixes.
        var priors = Priors(
            Prior(1, ("TOP", 0.55), ("JUNGLE", 0.44)),
            Prior(2, ("TOP", 0.60), ("MIDDLE", 0.39)));

        var unpinned = LaneAssignmentSolver.Solve([1, 2], priors);
        Assert.Equal("TOP", unpinned.Single(a => a.ChampionId == 2).Position);

        var corrected = LaneAssignmentSolver.Solve(
            [1, 2], priors, pinned: new Dictionary<int, string> { [1] = "TOP" });

        Assert.Equal("TOP", corrected.Single(a => a.ChampionId == 1).Position);
        Assert.Equal("MIDDLE", corrected.Single(a => a.ChampionId == 2).Position);
    }

    [Fact]
    public void AChampionWeHaveNeverSeenDoesNotDragTheOthersOutOfPlace()
    {
        // An unknown champion must cost the same everywhere, so the champions we
        // do know keep the lanes their priors ask for.
        var priors = Priors(Dedicated(1, "MIDDLE"), Dedicated(2, "UTILITY"));

        var result = LaneAssignmentSolver.Solve([1, 2, 999], priors);

        Assert.Equal("MIDDLE", result.Single(a => a.ChampionId == 1).Position);
        Assert.Equal("UTILITY", result.Single(a => a.ChampionId == 2).Position);
    }

    [Fact]
    public void AChampionNeverSeenInALaneStillTakesItRatherThanBreakingTheDraft()
    {
        // The floor on a zero-probability lane: without it this placement scores
        // negative infinity and the solver has no answer at all.
        var priors = Priors(Prior(1, ("MIDDLE", 1.0)), Prior(2, ("MIDDLE", 1.0)));

        var result = LaneAssignmentSolver.Solve([1, 2], priors);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(a => a.Position).Distinct().Count());
    }

    [Fact]
    public void AnEvenSplitReadsAsACoinFlipRatherThanAConfidentCall()
    {
        var priors = Priors(Prior(1, ("TOP", 0.50), ("MIDDLE", 0.50)));

        var result = LaneAssignmentSolver.Solve([1], priors);

        Assert.Equal(0.5d, result.Single().Confidence, precision: 3);
    }

    [Fact]
    public void AnUnambiguousPlacementReadsAsConfident()
    {
        var priors = Priors(Prior(1, ("JUNGLE", 0.99), ("TOP", 0.01)));

        Assert.True(LaneAssignmentSolver.Solve([1], priors).Single().Confidence > 0.9);
    }

    [Fact]
    public void KeepsThePlacementOnScreenWhenTwoReadingsScoreTheSame()
    {
        // Equally-good placements must not swap under the reader between two
        // picks; the one already displayed wins.
        var priors = Priors(Prior(1, ("TOP", 0.50), ("MIDDLE", 0.50)));
        var onScreen = new Dictionary<int, string> { [1] = "MIDDLE" };

        var result = LaneAssignmentSolver.Solve([1], priors, previous: onScreen);

        Assert.Equal("MIDDLE", result.Single().Position);
    }

    [Fact]
    public void ContradictoryPinsFallBackToAnUnpinnedAnswerRatherThanNothing()
    {
        var priors = Priors(Dedicated(1, "TOP"), Dedicated(2, "MIDDLE"));

        var result = LaneAssignmentSolver.Solve(
            [1, 2],
            priors,
            pinned: new Dictionary<int, string> { [1] = "TOP", [2] = "TOP" });

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(a => a.Position).Distinct().Count());
        Assert.All(result, a => Assert.False(a.Pinned));
    }

    [Fact]
    public void DeduplicatesARepeatedChampionRatherThanClaimingTwoLanes()
    {
        var result = LaneAssignmentSolver.Solve([7, 7], Priors(Dedicated(7, "TOP")));

        Assert.Single(result);
    }

    [Fact]
    public void AChampionSeenOnlyOutsideThePatchWindowReadsAsUnseenNotAsImpossibleEverywhere()
    {
        // The prior keeps `Games` for evidence but empties the map when every
        // row is older than the window. That must cost the same as an unknown
        // champion — a flat constant — not the lane floor on every lane.
        var stale = new LanePrior { ChampionId = 1, ByLane = new Dictionary<string, double>(), Games = 500 };
        var unseen = new LanePrior { ChampionId = 1, ByLane = new Dictionary<string, double>(), Games = 0 };

        var withStale = LaneAssignmentSolver.Solve([1, 2], Priors(stale, Dedicated(2, "MIDDLE")));
        var withUnseen = LaneAssignmentSolver.Solve([1, 2], Priors(unseen, Dedicated(2, "MIDDLE")));

        Assert.Equal("MIDDLE", withStale.Single(a => a.ChampionId == 2).Position);
        Assert.Equal(
            withUnseen.Single(a => a.ChampionId == 1).Confidence,
            withStale.Single(a => a.ChampionId == 1).Confidence,
            precision: 9);
    }

    [Fact]
    public void APinOnAChampionNoLongerInTheDraftDoesNotDiscardTheOtherPins()
    {
        // Champion 1 wants MIDDLE; the user pinned it TOP, and also pinned a
        // champion that has since left the draft. The stale pin is ignored on
        // its own; the live one still holds.
        var priors = Priors(Dedicated(1, "MIDDLE"), Dedicated(2, "JUNGLE"));

        var result = LaneAssignmentSolver.Solve(
            [1, 2],
            priors,
            pinned: new Dictionary<int, string> { [1] = "TOP", [99] = "BOTTOM" });

        var first = result.Single(a => a.ChampionId == 1);
        Assert.Equal("TOP", first.Position);
        Assert.True(first.Pinned);
    }

    [Fact]
    public void MoreChampionsThanLanesYieldsNoPlacementRatherThanThrowing()
    {
        var result = LaneAssignmentSolver.Solve([1, 2, 3, 4, 5, 6], Priors());

        Assert.Empty(result);
    }
}
