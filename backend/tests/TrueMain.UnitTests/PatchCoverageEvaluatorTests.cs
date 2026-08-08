using AwesomeAssertions;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The patch-coverage verdict (#1033) exists so an operator does not have to eyeball the
/// public pages, so what these tests pin is the ways it could mislead: collapsing "nothing
/// aggregated it yet" into "it is thin", going green on a patch nothing has measured, or
/// letting the still-filling patch set its own bar.
/// </summary>
public sealed class PatchCoverageEvaluatorTests
{
    [Fact]
    public void ReadVerdict_KeepsAnUnaggregatedPatchApartFromAThinOne()
    {
        // The whole point of the view. Both report zero servable lines and they call for
        // opposite reactions: one is a fold that has not run, the other is a patch that
        // genuinely lacks games.
        var unaggregated = PatchCoverageEvaluator.ReadVerdict(matches: 500, lines: 0, linesPastFloor: 0, bar: 100, isServed: false);
        var thin = PatchCoverageEvaluator.ReadVerdict(matches: 500, lines: 40, linesPastFloor: 3, bar: 100, isServed: false);

        unaggregated.Verdict.Should().Be("notAggregated");
        thin.Verdict.Should().Be("thin");
    }

    [Fact]
    public void ReadVerdict_ReadsAnEmptyPatchAsUnknown_NeverAsAPass()
    {
        var verdict = PatchCoverageEvaluator.ReadVerdict(matches: 0, lines: 0, linesPastFloor: 0, bar: 100, isServed: false);

        // Nothing was ingested and nothing was aggregated, so there is no reading. Green
        // here would be a dashboard reporting health on a patch it never measured.
        verdict.Verdict.Should().Be("unknown");
        verdict.Status.Should().Be(DetectorStatus.Unknown);
        verdict.Judged.Should().BeFalse("an unjudged patch must not print a bar it was never compared against");
    }

    [Fact]
    public void ReadVerdict_RaisesAThinPatchToRed_OnlyWhenItIsTheOneBeingServed()
    {
        var served = PatchCoverageEvaluator.ReadVerdict(matches: 500, lines: 40, linesPastFloor: 3, bar: 100, isServed: true);
        var historical = PatchCoverageEvaluator.ReadVerdict(matches: 500, lines: 40, linesPastFloor: 3, bar: 100, isServed: false);

        // A thin patch nobody reads is history; a thin patch behind today's tier list is
        // the site publishing a ranking it cannot support.
        served.Status.Should().Be(DetectorStatus.Red);
        historical.Status.Should().Be(DetectorStatus.Amber);
    }

    [Fact]
    public void ReadVerdict_ClearsTheBarOnEquality()
    {
        // Reaching the bar is `>=`, matching every other threshold on the ops panels. An
        // off-by-one here reads as "one line short" on a patch that is exactly at the bar.
        PatchCoverageEvaluator
            .ReadVerdict(matches: 500, lines: 200, linesPastFloor: 100, bar: 100, isServed: true)
            .Verdict.Should().Be("servable");
    }

    [Fact]
    public void ReadBar_TakesTheConfiguredShareOfTheSettledMedian()
    {
        var bar = PatchCoverageEvaluator.ReadBar([100, 200, 300], ratio: 0.6, minimum: 42, servedPatch: "16.15");

        bar.Reference.Should().Be(200);
        bar.Value.Should().BeApproximately(120, 0.001);
        bar.ReferencePatches.Should().Be(3);
        bar.Note.Should().Contain("16.15", "a bar with no provenance is not an answer");
    }

    [Fact]
    public void ReadBar_FallsBackToTheConfiguredMinimum_WhenNoSettledPatchExists()
    {
        // A database holding a single patch — preprod's normal state. The fallback is
        // crude, and it is still an answer rather than a shrug.
        var bar = PatchCoverageEvaluator.ReadBar([], ratio: 0.6, minimum: 42, servedPatch: "16.15");

        bar.Value.Should().Be(42);
        bar.Reference.Should().BeNull();
        bar.Note.Should().Contain("No settled patch");
    }

    [Fact]
    public void ReadBar_NeverYieldsANegativeBar_OnAMisconfiguredRatio()
    {
        // Options validation rejects a ratio outside (0, 1], but the evaluator is the
        // thing under test elsewhere and a negative bar would make every patch servable.
        PatchCoverageEvaluator.ReadBar([100], ratio: -1, minimum: 42, servedPatch: null)
            .Value.Should().Be(0);
    }

    [Fact]
    public void Median_AveragesTheTwoMiddlesOnAnEvenCount()
    {
        PatchCoverageEvaluator.Median([10, 20]).Should().Be(15);
        PatchCoverageEvaluator.Median([30, 10, 20]).Should().Be(20);
    }

    [Fact]
    public void Median_ReportsNothingToCompareAgainst_RatherThanZero()
    {
        // Zero would be a bar that every patch clears, including a completely empty one.
        PatchCoverageEvaluator.Median([]).Should().BeNull();
    }
}
