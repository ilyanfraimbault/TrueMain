using Core.Lol.Draft;
using Data.Entities;
using Data.ItemContext;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;

namespace TrueMain.UnitTests;

/// <summary>
/// The draft's team-composition term (#1906): its arithmetic, how the board is placed on
/// the ally axes, the per-champion patch fallback, and how a card states it while its
/// weight is 0.
/// </summary>
public class DraftTeamFitTests
{
    private static readonly DraftScoringWeights Weights = DraftScoringWeights.Default;
    private static readonly DraftPatchScope Scope = new("16.19", "16.18");
    private static readonly Dictionary<int, double> None = [];

    [Fact]
    public void AnAxisIsTheBandsWinRateMinusTheOverallOneOverTheBandsGames()
    {
        var fit = DraftScoring.Fit(bandGames: 400, bandWins: 220, overallGames: 2_000, overallWins: 1_000);

        Assert.Equal(0.05, fit.Delta, 9);
        Assert.Equal(400, fit.Games);
        Assert.Equal(DraftComponent.None, DraftScoring.Fit(0, 0, 2_000, 1_000));
        Assert.Equal(DraftComponent.None, DraftScoring.Fit(400, 220, 0, 0));
    }

    [Fact]
    public void TheTermIsAMeanOverThePlacedAxesAnUnmeasuredOneCountingAsNoEffect()
    {
        var (measured, shrunk) = DraftScoring.TeamFit([new DraftComponent(0.04, 1_000), DraftComponent.None], 0d);

        Assert.Equal(0.04, measured.Delta, 9);
        Assert.Equal(1_000, measured.Games);
        Assert.Equal(0.02, shrunk, 9);
        Assert.Equal((DraftComponent.None, 0d), DraftScoring.TeamFit([], 400d));
    }

    [Fact]
    public void TheTermOnlyMovesTheScoreWithAWeight()
    {
        Assert.Equal(0d, Weights.TeamFit);
        Assert.Equal(
            DraftScoring.Score(0d, 1d, 0d, 0d, 0d, Weights),
            DraftScoring.Score(0d, 1d, 0d, 0d, 0d, Weights, teamFitShrunk: 0.05));
        Assert.True(DraftScoring.Score(0d, 1d, 0d, 0d, 0d, Weights with { TeamFit = 0.5 }, teamFitShrunk: 0.05) > 0d);
    }

    [Fact]
    public void ThreeAlliesPlaceTheTeamAndAMagicHeavyOneReadsHigh()
    {
        var profiles = Profiles(
            Profile(64, "JUNGLE", magic: 0.8, tank: 0.9),
            Profile(40, "UTILITY", magic: 0.9, tank: 0.8),
            Profile(22, "BOTTOM", magic: 0.6, tank: 0.1));

        var bands = DraftTeamFitReader.Place(
            [new(64, "JUNGLE", false), new(40, "UTILITY", true), new(22, "BOTTOM", false)], profiles);

        Assert.Equal(ItemContextBucket.High, bands[ItemContextAxis.AllyMagicDamage]);
        Assert.Equal(ItemContextBucket.High, bands[ItemContextAxis.AllyFrontline]);
    }

    [Fact]
    public void TwoMissingAlliesPlaceNothing()
    {
        // One ally not picked yet, one with no profile: two unknowns, as the fold would drop.
        var profiles = Profiles(Profile(64, "JUNGLE", magic: 0.1, tank: 0.9), Profile(40, "UTILITY", magic: 0.1, tank: 0.9));

        var bands = DraftTeamFitReader.Place(
            [new(64, "JUNGLE", false), new(40, "UTILITY", false), new(999, "BOTTOM", false)], profiles);

        Assert.Empty(bands);
    }

    [Fact]
    public void ThePreviousPatchIsAddedOnlyForAChampionTheCurrentOneIsThinOn()
    {
        var bands = new Dictionary<ItemContextAxis, ItemContextBucket> { [ItemContextAxis.AllyMagicDamage] = ItemContextBucket.Low };
        List<DraftTeamFitReader.TotalRow> thick =
        [
            Row("16.19", ItemContextAxis.Overall, ItemContextBucket.All, 1_000, 500),
            Row("16.19", ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, 200, 120),
            Row("16.19", ItemContextAxis.AllyMagicDamage, ItemContextBucket.High, 300, 100),
            Row("16.18", ItemContextAxis.Overall, ItemContextBucket.All, 5_000, 2_000),
            Row("16.18", ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, 1_000, 100),
        ];

        var current = DraftTeamFitReader.Build(thick, bands, Scope);

        var axis = Assert.Single(current.Axes);
        Assert.Equal(0.1, axis.Fit.Delta, 9);
        Assert.Equal(200, axis.Fit.Games);
        Assert.Equal("16.19", current.Patch);

        List<DraftTeamFitReader.TotalRow> thin =
        [
            Row("16.19", ItemContextAxis.Overall, ItemContextBucket.All, 100, 50),
            Row("16.19", ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, 50, 30),
            Row("16.18", ItemContextAxis.Overall, ItemContextBucket.All, 900, 450),
            Row("16.18", ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, 150, 90),
        ];

        var summed = DraftTeamFitReader.Build(thin, bands, Scope);

        Assert.Equal(0.1, summed.Axes[0].Fit.Delta, 9);
        Assert.Equal(200, summed.Axes[0].Fit.Games);
        Assert.Equal("16.19+16.18", summed.Patch);
    }

    [Fact]
    public void WhileItsWeightIsZeroTheCompositionIsStatedAfterTheScoredReasons()
    {
        var record = new DraftLaneRecord(
            2_000,
            1_000,
            new Dictionary<int, DraftComponent> { [10] = new(0.05, 2_000) },
            new Dictionary<int, (int, int)> { [10] = (600, 300) },
            "16.19");
        var occupancy = new Dictionary<int, double> { [10] = 1d };
        var allies = new List<DraftAllyInput>
        {
            new(30, "UTILITY", Hovered: false, new DraftComponent(0.03, 2_000)),
            new(31, "JUNGLE", Hovered: false, new DraftComponent(0.02, 2_000)),
        };
        var fit = new DraftTeamFit(
            [
                new DraftTeamFitAxis(ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, new DraftComponent(-0.03, 1_500)),
                new DraftTeamFitAxis(ItemContextAxis.AllyFrontline, ItemContextBucket.Mid, new DraftComponent(0.01, 1_500)),
            ],
            Tentative: false,
            "16.19");

        var stated = DraftCandidateScorer.Score(1, record, occupancy, None, 0.5, allies, Weights, teamFit: fit);
        var without = DraftCandidateScorer.Score(1, record, occupancy, None, 0.5, allies, Weights);

        Assert.Equal(without.Score, stated.Score);
        Assert.Equal(-0.01, stated.TeamFitDelta, 9);
        Assert.Equal(1_500, stated.TeamFitGames);
        // Three scored reasons, then the facts that are not score terms: the lane phase,
        // and the composition axis that moves the pick most.
        Assert.Equal(
            [DraftReasonKinds.LaneMatchup, DraftReasonKinds.Synergy, DraftReasonKinds.Synergy, DraftReasonKinds.LanePhase, DraftReasonKinds.TeamFit],
            stated.Reasons.Select(reason => reason.Kind));
        var reason = stated.Reasons[^1];
        Assert.Equal("AllyMagicDamage", reason.Axis);
        Assert.Equal("Low", reason.Bucket);
        Assert.Equal(-0.03, reason.Delta!.Value, 9);
        Assert.Equal(1_500, reason.Games);
    }

    [Fact]
    public void AThinCompositionReadingIsNotStated()
    {
        var fit = new DraftTeamFit(
            [new DraftTeamFitAxis(ItemContextAxis.AllyMagicDamage, ItemContextBucket.High, new DraftComponent(0.2, DraftCandidateScorer.MinGames - 1))],
            Tentative: false,
            "16.19");

        var candidate = DraftCandidateScorer.Score(1, DraftLaneRecord.Empty, None, None, 0.5, [], Weights, teamFit: fit);

        Assert.Empty(candidate.Reasons);
    }

    [Fact]
    public void WithAWeightTheCompositionIsAScoredReason()
    {
        var fit = new DraftTeamFit(
            [new DraftTeamFitAxis(ItemContextAxis.AllyFrontline, ItemContextBucket.Low, new DraftComponent(0.04, 4_000))],
            Tentative: true,
            "16.19");

        var candidate = DraftCandidateScorer.Score(
            1, DraftLaneRecord.Empty, None, None, 0.5, [], Weights with { TeamFit = 0.5 }, teamFit: fit);

        Assert.True(candidate.Score > 0d);
        var reason = Assert.Single(candidate.Reasons);
        Assert.Equal(DraftReasonKinds.TeamFit, reason.Kind);
        Assert.True(reason.Tentative);
    }

    private static DraftTeamFitReader.TotalRow Row(string patch, ItemContextAxis axis, ItemContextBucket bucket, int games, int wins)
        => new(1, patch, axis, bucket, games, wins);

    private static ChampionProfileSnapshot Profiles(params ChampionProfileStat[] rows)
        => ChampionProfileSnapshot.FromRows(rows, "16.19", ChampionProfileSnapshotRules.LookbackPatches, ChampionProfileSnapshotRules.MinGames);

    private static ChampionProfileStat Profile(int championId, string position, double magic, double tank) => new()
    {
        ChampionId = championId,
        Position = position,
        Patch = "16.19",
        Games = 1_000,
        GameDurationSecondsSum = 1_800_000,
        MagicDamageToChampionsSum = (long)(magic * 20_000_000),
        PhysicalDamageToChampionsSum = (long)((1 - magic) * 20_000_000),
        ItemGames = 1_000,
        TankGames = (int)(tank * 1_000),
    };
}
