using AwesomeAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class CompositionSimilarityScorerTests
{
    private static readonly CompositionScoreWeights Weights = new(LaneOpponent: 10, Enemy: 4, Ally: 2);

    private static CompositionSearchCriteria Criteria(
        IReadOnlyDictionary<string, int>? allies = null,
        IReadOnlyDictionary<string, int>? enemies = null)
        => new()
        {
            ChampionId = 157, // Yone
            Position = "MIDDLE",
            Allies = allies ?? new Dictionary<string, int>(),
            Enemies = enemies ?? new Dictionary<string, int>(),
        };

    [Fact]
    public void Score_LaneOpponentMatch_CountsLaneOpponentWeight()
    {
        var criteria = Criteria(enemies: new Dictionary<string, int> { ["MIDDLE"] = 238 });

        var score = CompositionSimilarityScorer.Score(
            criteria,
            Weights,
            [new CompositionSlot(IsEnemy: true, "MIDDLE", 238)]);

        score.Should().Be(10);
    }

    [Fact]
    public void Score_OtherEnemyMatch_CountsEnemyWeight()
    {
        var criteria = Criteria(enemies: new Dictionary<string, int> { ["TOP"] = 266 });

        var score = CompositionSimilarityScorer.Score(
            criteria,
            Weights,
            [new CompositionSlot(IsEnemy: true, "TOP", 266)]);

        score.Should().Be(4);
    }

    [Fact]
    public void Score_AllyMatch_CountsAllyWeight()
    {
        var criteria = Criteria(allies: new Dictionary<string, int> { ["JUNGLE"] = 64 });

        var score = CompositionSimilarityScorer.Score(
            criteria,
            Weights,
            [new CompositionSlot(IsEnemy: false, "JUNGLE", 64)]);

        score.Should().Be(2);
    }

    [Fact]
    public void Score_WrongChampionInRequestedSlot_CountsNothing()
    {
        var criteria = Criteria(enemies: new Dictionary<string, int> { ["MIDDLE"] = 238 });

        var score = CompositionSimilarityScorer.Score(
            criteria,
            Weights,
            [new CompositionSlot(IsEnemy: true, "MIDDLE", 91)]);

        score.Should().Be(0);
    }

    [Fact]
    public void Score_RequestedChampionOnWrongSide_CountsNothing()
    {
        // Zed requested as the lane opponent, but the candidate game has him
        // as the MIDDLE ally-side row — sides are not interchangeable.
        var criteria = Criteria(enemies: new Dictionary<string, int> { ["MIDDLE"] = 238 });

        var score = CompositionSimilarityScorer.Score(
            criteria,
            Weights,
            [new CompositionSlot(IsEnemy: false, "MIDDLE", 238)]);

        score.Should().Be(0);
    }

    [Fact]
    public void Score_AllyEntryAtPlayerPosition_IsIgnored()
    {
        // The player's own slot is the hard filter; a stray Allies entry
        // there must not score even if a row matches it.
        var criteria = Criteria(allies: new Dictionary<string, int> { ["MIDDLE"] = 157 });

        var score = CompositionSimilarityScorer.Score(
            criteria,
            Weights,
            [new CompositionSlot(IsEnemy: false, "MIDDLE", 157)]);

        score.Should().Be(0);
    }

    [Fact]
    public void Score_FullDraftReproduced_SumsEverySlot()
    {
        var criteria = Criteria(
            allies: new Dictionary<string, int>
            {
                ["TOP"] = 86,
                ["JUNGLE"] = 64,
                ["BOTTOM"] = 22,
                ["UTILITY"] = 412,
            },
            enemies: new Dictionary<string, int>
            {
                ["TOP"] = 266,
                ["JUNGLE"] = 121,
                ["MIDDLE"] = 238,
                ["BOTTOM"] = 51,
                ["UTILITY"] = 555,
            });

        var slots = new[]
        {
            new CompositionSlot(IsEnemy: false, "TOP", 86),
            new CompositionSlot(IsEnemy: false, "JUNGLE", 64),
            new CompositionSlot(IsEnemy: false, "BOTTOM", 22),
            new CompositionSlot(IsEnemy: false, "UTILITY", 412),
            new CompositionSlot(IsEnemy: true, "TOP", 266),
            new CompositionSlot(IsEnemy: true, "JUNGLE", 121),
            new CompositionSlot(IsEnemy: true, "MIDDLE", 238),
            new CompositionSlot(IsEnemy: true, "BOTTOM", 51),
            new CompositionSlot(IsEnemy: true, "UTILITY", 555),
        };

        var score = CompositionSimilarityScorer.Score(criteria, Weights, slots);

        // 10 (lane opponent) + 4×4 (other enemies) + 2×4 (allies).
        score.Should().Be(34);
        score.Should().Be(CompositionSimilarityScorer.MaxScore(criteria, Weights));
    }

    [Fact]
    public void Score_PartialDraft_OnlyRequestedSlotsCount()
    {
        var criteria = Criteria(
            enemies: new Dictionary<string, int> { ["MIDDLE"] = 238, ["JUNGLE"] = 121 });

        var slots = new[]
        {
            new CompositionSlot(IsEnemy: true, "MIDDLE", 238), // requested, matches → 10
            new CompositionSlot(IsEnemy: true, "JUNGLE", 60), // requested, differs → 0
            new CompositionSlot(IsEnemy: true, "TOP", 266), // never requested → 0
            new CompositionSlot(IsEnemy: false, "JUNGLE", 64), // never requested → 0
        };

        CompositionSimilarityScorer.Score(criteria, Weights, slots).Should().Be(10);
    }

    [Fact]
    public void MaxScore_EmptyDraft_IsZero()
    {
        CompositionSimilarityScorer.MaxScore(Criteria(), Weights).Should().Be(0);
    }

    [Fact]
    public void MaxScore_CountsRequestedSlotsByKind()
    {
        var criteria = Criteria(
            allies: new Dictionary<string, int> { ["TOP"] = 86, ["MIDDLE"] = 157 },
            enemies: new Dictionary<string, int> { ["MIDDLE"] = 238, ["BOTTOM"] = 51 });

        // Enemy MIDDLE is the lane opponent (10), enemy BOTTOM 4, ally TOP 2;
        // the ally entry at the player's own position is ignored.
        CompositionSimilarityScorer.MaxScore(criteria, Weights).Should().Be(16);
    }
}
