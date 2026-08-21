using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers the read-time recall derivation (#1186): the strict first-to-last-step
/// purchase window, the start-of-game shopping floor, the ITEM_PURCHASED-only
/// rule (jungler ITEM_DESTROYED artifacts), same-visit clustering, the
/// one-recall-per-gap cap and the step-gap bracketing.
/// </summary>
public sealed class JungleClearRecallsTests
{
    // A six-step clear, one camp per minute from 1:00 to 6:00.
    private static readonly List<JungleClearStep> FullClear = Steps(
        ("BlueGromp", 1),
        ("BlueBlueBuff", 2),
        ("BlueWolves", 3),
        ("BlueRaptors", 4),
        ("BlueRedBuff", 5),
        ("BlueKrugs", 6));

    [Fact]
    public void Derive_FewerThanTwoSteps_YieldsNoRecall()
    {
        var oneStep = Steps(("BlueGromp", 1));

        JungleClearRecalls.Derive(oneStep, Purchases(90_000)).Should().BeEmpty();
        JungleClearRecalls.Derive(new List<JungleClearStep>(), Purchases(90_000)).Should().BeEmpty();
    }

    [Fact]
    public void Derive_NoPurchaseInWindow_YieldsNoRecall()
    {
        JungleClearRecalls.Derive(FullClear, Purchases()).Should().BeEmpty();
    }

    [Fact]
    public void Derive_PurchaseBetweenSteps_YieldsOneRecall_InTheBracketingGap()
    {
        // 3:30 sits between step 2 (3:00, index 2) and step 3 (4:00).
        var recalls = JungleClearRecalls.Derive(FullClear, Purchases(210_000));

        recalls.Should().HaveCount(1);
        recalls[0].TimestampMs.Should().Be(210_000);
        recalls[0].AfterStepIndex.Should().Be(2);
    }

    [Fact]
    public void Derive_WindowBoundsAreStrict()
    {
        // Purchases exactly on the first step, the last step, before the clear
        // and after it are all outside the mid-clear window.
        var recalls = JungleClearRecalls.Derive(
            FullClear,
            Purchases(30_000, 60_000, 360_000, 400_000));

        recalls.Should().BeEmpty();
    }

    [Fact]
    public void Derive_StartOfGameShoppingFloor_AppliesWhenFirstStepIsEarlier()
    {
        // Contrived sub-minute first step: a 50 s purchase is inside the
        // steps window but below the 60 s floor — still start-of-game shopping.
        var steps = StepsAt(
            ("BlueRedBuff", 40_000),
            ("BlueKrugs", 150_000));

        JungleClearRecalls.Derive(steps, Purchases(50_000)).Should().BeEmpty();
        JungleClearRecalls.Derive(steps, Purchases(90_000)).Should().ContainSingle()
            .Which.AfterStepIndex.Should().Be(0);
    }

    [Fact]
    public void Derive_IgnoresNonPurchaseEvents()
    {
        var events = new List<ItemEvent>
        {
            Event("ITEM_DESTROYED", 210_000),
            Event("ITEM_SOLD", 215_000),
            Event("ITEM_UNDO", 220_000),
        };

        JungleClearRecalls.Derive(FullClear, events).Should().BeEmpty();
    }

    [Fact]
    public void Derive_ClustersPurchasesWithinThirtySeconds_KeepingTheEarliest()
    {
        // One back at 3:10: item + ward + refillable within 30 s of each other.
        var recalls = JungleClearRecalls.Derive(
            FullClear,
            Purchases(190_000, 210_000, 235_000));

        recalls.Should().HaveCount(1);
        recalls[0].TimestampMs.Should().Be(190_000);
        recalls[0].AfterStepIndex.Should().Be(2);
    }

    [Fact]
    public void Derive_DistinctVisitsInDifferentGaps_YieldTwoRecalls()
    {
        // 2:30 (between steps 1 and 2) and 4:30 (between steps 3 and 4).
        var recalls = JungleClearRecalls.Derive(FullClear, Purchases(150_000, 270_000));

        recalls.Should().HaveCount(2);
        recalls[0].AfterStepIndex.Should().Be(1);
        recalls[1].AfterStepIndex.Should().Be(3);
    }

    [Fact]
    public void Derive_TwoVisitsInTheSameGap_MergeToTheEarliest()
    {
        // Both clusters land between step 2 (3:00) and step 3 (4:00).
        var recalls = JungleClearRecalls.Derive(FullClear, Purchases(185_000, 225_000));

        recalls.Should().HaveCount(1);
        recalls[0].TimestampMs.Should().Be(185_000);
    }

    [Fact]
    public void Derive_PurchaseOnAnIntermediateStepTimestamp_BracketsToThatStep()
    {
        var recalls = JungleClearRecalls.Derive(FullClear, Purchases(240_000));

        recalls.Should().ContainSingle().Which.AfterStepIndex.Should().Be(3);
    }

    [Fact]
    public void Derive_DuplicateStepTimestamps_KeepBracketingStable()
    {
        // Two camps detected on the same frame (3:00) — a real minute-resolution
        // case. A 3:30 purchase must bracket to the later of the pair.
        var steps = StepsAt(
            ("BlueGromp", 60_000),
            ("BlueBlueBuff", 180_000),
            ("BlueWolves", 180_000),
            ("BlueRaptors", 240_000));

        var recalls = JungleClearRecalls.Derive(steps, Purchases(210_000));

        recalls.Should().ContainSingle().Which.AfterStepIndex.Should().Be(2);
    }

    private static List<JungleClearStep> Steps(params (string Camp, int Minute)[] steps)
        => StepsAt(steps.Select(s => (s.Camp, s.Minute * 60_000)).ToArray());

    private static List<JungleClearStep> StepsAt(params (string Camp, int TimestampMs)[] steps)
        => steps
            .Select(s => new JungleClearStep { Camp = s.Camp, TimestampMs = s.TimestampMs })
            .ToList();

    private static List<ItemEvent> Purchases(params int[] timestamps)
        => timestamps.Select(t => Event("ITEM_PURCHASED", t)).ToList();

    private static ItemEvent Event(string eventType, int timestampMs)
        => new() { EventType = eventType, TimestampMs = timestampMs, ItemId = 1001 };
}
