using AwesomeAssertions;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes.Components.ItemContextAggregation;

namespace TrueMain.UnitTests;

/// <summary>
/// The verdict rule of #1450, one clause at a time: what makes an item Core, Situational or
/// a Preference, the three floors an axis has to clear together, how a thin bucket widens
/// backwards, and which end of an axis a finding points at.
/// </summary>
public sealed class ItemContextVerdictBuilderTests
{
    private const int Champion = 266;
    private const string Position = "TOP";
    private const string Patch = "16.4";
    private const string Previous = "16.3";
    private const int Item = 3068;

    private static readonly DateTime Now = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnItemBuiltInNearlyEveryGameIsCore_AndNoSituationIsLookedFor()
    {
        var verdict = Single(
            options: Options(),
            stats: [Overall(940), Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 500),
                    Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 440)],
            totals: [OverallTotal(1000), Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 500),
                     Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 500)]);

        verdict.Class.Should().Be(ItemContextClass.Core);
        verdict.PickRate.Should().BeApproximately(0.94, 1e-9);
        verdict.Axes.Should().BeEmpty();
    }

    [Fact]
    public void AnAxisThatMovesThePickRateMakesTheItemSituational()
    {
        var verdict = Single(
            options: Options(),
            stats: [Overall(400), Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 310),
                    Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 90)],
            totals: [OverallTotal(1000), Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 500),
                     Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 500)]);

        verdict.Class.Should().Be(ItemContextClass.Situational);
        var finding = verdict.Axes.Should().ContainSingle().Subject;
        finding.Axis.Should().Be(ItemContextAxis.EnemyMagicDamage);
        finding.Bucket.Should().Be(ItemContextBucket.High);
        finding.RateIn.Should().BeApproximately(0.62, 1e-9);
        finding.RateOut.Should().BeApproximately(0.18, 1e-9);
        finding.Lift.Should().BeApproximately(0.44, 1e-9);
        finding.PatchWindow.Should().Be(1);
    }

    [Fact]
    public void TheFindingPointsAtWhicheverEndBuildsItMore()
    {
        // Built against ranged lanes, i.e. at the Low end of the melee-count axis.
        var verdict = Single(
            options: Options(),
            stats: [Overall(400), Stat(ItemContextAxis.EnemyMelee, ItemContextBucket.High, 90),
                    Stat(ItemContextAxis.EnemyMelee, ItemContextBucket.Low, 310)],
            totals: [OverallTotal(1000), Total(ItemContextAxis.EnemyMelee, ItemContextBucket.High, 500),
                     Total(ItemContextAxis.EnemyMelee, ItemContextBucket.Low, 500)]);

        var finding = verdict.Axes.Should().ContainSingle().Subject;
        finding.Bucket.Should().Be(ItemContextBucket.Low);
        finding.Lift.Should().BePositive("the lift is oriented, so a sentence never has to invert itself");
    }

    [Fact]
    public void AnItemNoAxisMovesIsAPreference()
    {
        var verdict = Single(
            options: Options(),
            stats: [Overall(400), Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 205),
                    Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 195)],
            totals: [OverallTotal(1000), Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 500),
                     Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 500)]);

        verdict.Class.Should().Be(ItemContextClass.Preference);
        verdict.Axes.Should().BeEmpty();
    }

    [Fact]
    public void ASignificantButTinyGapIsNotAnExplanation()
    {
        // 52% against 48% over 25 000 games a side: unmistakably significant, and four
        // points is not a reason to build something.
        var verdict = Single(
            options: Options(),
            stats: [Overall(25_000), Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 13_000),
                    Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 12_000)],
            totals: [OverallTotal(50_000), Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 25_000),
                     Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 25_000)]);

        verdict.Class.Should().Be(ItemContextClass.Preference);
    }

    [Fact]
    public void AGapTheSampleCannotSupportIsNotAnExplanationEither()
    {
        // 60% against 50% over 120 games a side: it clears the absolute-lift floor exactly,
        // and the sample cannot tell it apart from chance (|z| ~ 1.6). The two floors are
        // both needed, and this is the case only the second one catches.
        var verdict = Single(
            options: Options(),
            stats: [Overall(132), Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 72),
                    Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 60)],
            totals: [OverallTotal(1000), Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 120),
                     Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 120)]);

        verdict.Class.Should().Be(ItemContextClass.Preference);
    }

    [Fact]
    public void AThinBucketWidensBackwardsRatherThanBeingDropped()
    {
        var verdict = Single(
            options: Options(),
            patchWindow: [Patch, Previous],
            stats:
            [
                Overall(80),
                Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 62),
                Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 18),
                Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 248, Previous),
                Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 72, Previous),
            ],
            totals:
            [
                OverallTotal(200),
                Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 60),
                Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 60),
                Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 240, Previous),
                Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 240, Previous),
            ]);

        var finding = verdict.Axes.Should().ContainSingle().Subject;
        finding.PatchWindow.Should().Be(2);
        finding.TotalIn.Should().Be(300, "both ends widen together or the two rates are not comparable");
        finding.TotalOut.Should().Be(300);
        verdict.PatchWindow.Should().Be(2);
        verdict.SlotGames.Should().Be(200, "the class and the pick rate still describe the served patch alone");
    }

    [Fact]
    public void AnAxisStillThinAfterWideningIsDropped()
    {
        var verdict = Single(
            options: Options(),
            patchWindow: [Patch, Previous],
            stats: [Overall(80), Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 40),
                    Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 5)],
            totals: [OverallTotal(200), Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 50),
                     Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 50)]);

        verdict.Axes.Should().BeEmpty();
        verdict.Class.Should().Be(ItemContextClass.Preference);
    }

    [Fact]
    public void AnItemTheSliceBarelyEverBuildsGetsNoVerdictAtAll()
    {
        var verdicts = Build(
            Options(),
            [Overall(20)],
            [OverallTotal(1000)],
            [Patch]);

        verdicts.Should().BeEmpty("2% is not a decision worth a card");
    }

    [Fact]
    public void FindingsAreRankedByLiftAndCapped()
    {
        var options = Options();
        options.MaxAxesPerVerdict = 2;

        var verdict = Single(
            options: options,
            stats:
            [
                Overall(400),
                Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 300),
                Stat(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 100),
                Stat(ItemContextAxis.EnemyCrowdControl, ItemContextBucket.High, 250),
                Stat(ItemContextAxis.EnemyCrowdControl, ItemContextBucket.Low, 150),
                Stat(ItemContextAxis.EnemyFrontline, ItemContextBucket.High, 230),
                Stat(ItemContextAxis.EnemyFrontline, ItemContextBucket.Low, 170),
            ],
            totals:
            [
                OverallTotal(1000),
                Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High, 500),
                Total(ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low, 500),
                Total(ItemContextAxis.EnemyCrowdControl, ItemContextBucket.High, 500),
                Total(ItemContextAxis.EnemyCrowdControl, ItemContextBucket.Low, 500),
                Total(ItemContextAxis.EnemyFrontline, ItemContextBucket.High, 500),
                Total(ItemContextAxis.EnemyFrontline, ItemContextBucket.Low, 500),
            ]);

        verdict.Axes.Should().HaveCount(2);
        verdict.Axes[0].Axis.Should().Be(ItemContextAxis.EnemyMagicDamage);
        verdict.Axes[1].Axis.Should().Be(ItemContextAxis.EnemyCrowdControl);
    }

    private static ItemContextAggregationOptions Options() => new()
    {
        MinBucketGames = 100,
        MinPickRate = 0.05,
        CoreRate = 0.85,
        MinAbsoluteLift = 0.10,
        MinAbsoluteZ = 1.96,
        MaxPatchLookback = 1,
        MaxAxesPerVerdict = 3,
    };

    private static ChampionItemContextVerdict Single(
        ItemContextAggregationOptions options,
        IReadOnlyList<ChampionItemContextStat> stats,
        IReadOnlyList<ChampionItemContextTotal> totals,
        IReadOnlyList<string>? patchWindow = null)
        => Build(options, stats, totals, patchWindow ?? [Patch]).Should().ContainSingle().Subject;

    private static IReadOnlyList<ChampionItemContextVerdict> Build(
        ItemContextAggregationOptions options,
        IReadOnlyList<ChampionItemContextStat> stats,
        IReadOnlyList<ChampionItemContextTotal> totals,
        IReadOnlyList<string> patchWindow)
        => ItemContextVerdictBuilder.Build(
            new ItemContextScope(Champion, Position, Patch), stats, totals, patchWindow, options, Now);

    private static ChampionItemContextStat Overall(int games)
        => Stat(ItemContextAxis.Overall, ItemContextBucket.All, games);

    private static ChampionItemContextStat Stat(
        ItemContextAxis axis, ItemContextBucket bucket, int games, string patch = Patch)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = patch,
            Slot = ItemContextSlot.Build,
            ItemId = Item,
            Axis = axis,
            Bucket = bucket,
            Games = games,
            Wins = games / 2,
        };

    private static ChampionItemContextTotal OverallTotal(int games)
        => Total(ItemContextAxis.Overall, ItemContextBucket.All, games);

    private static ChampionItemContextTotal Total(
        ItemContextAxis axis, ItemContextBucket bucket, int games, string patch = Patch)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = patch,
            Slot = ItemContextSlot.Build,
            Axis = axis,
            Bucket = bucket,
            Games = games,
            Wins = games / 2,
        };
}
