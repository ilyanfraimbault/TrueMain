using AwesomeAssertions;
using Ingestor.Processes.Components.Coverage;

namespace TrueMain.UnitTests;

/// <summary>
/// The region-balance rule (#1150). Every budget in the pipeline used to be one
/// cross-platform ordering, so each batch mirrored the pool it drew from — and each batch
/// fed that pool. These pin the properties the allocator has to hold for that loop to
/// converge instead of running away: no region is ever allocated out, an under-covered
/// region gets more, and the advantage decays as it catches up.
/// </summary>
public sealed class PlatformBudgetAllocatorTests
{
    private static readonly string[] Platforms = ["KR", "EUW1", "NA1"];

    [Fact]
    public void Allocate_SplitsEvenly_OnANeutralSnapshot()
    {
        // Cold start: no mains anywhere, so no reason to favour a region.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, 75, ChampionCoverageSnapshot.Empty);

        quotas.Values.Should().AllBeEquivalentTo(25);
        quotas.Values.Sum().Should().Be(75);
    }

    [Fact]
    public void Allocate_SplitsEvenly_WhenEveryPlatformIsAtTarget()
    {
        // The converged state. Balance is the fixed point, so once every region is covered
        // the allocation must stop moving budget around.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, 75, Snapshot(kr: 20, euw: 20, na: 20));

        quotas.Values.Should().AllBeEquivalentTo(25);
    }

    [Fact]
    public void Allocate_FavoursTheUnderCoveredPlatform()
    {
        // The measured prod state: EUW1 fully covered, KR barely covered.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, 75, Snapshot(kr: 1, euw: 20, na: 14));

        quotas["KR"].Should().BeGreaterThan(quotas["NA1"]);
        quotas["NA1"].Should().BeGreaterThan(quotas["EUW1"]);
        quotas.Values.Sum().Should().Be(75);
    }

    [Fact]
    public void Allocate_NeverStarvesACoveredPlatform()
    {
        // A covered region still needs its established mains re-ingested. Zeroing it would
        // just invert the imbalance instead of removing it, so weight bottoms out at 1 — the
        // most any deficit can buy is twice a covered platform's share, never all of it.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, 90, Snapshot(kr: 0, euw: 20, na: 20));

        quotas["EUW1"].Should().BeGreaterThan(0);
        quotas["NA1"].Should().BeGreaterThan(0);
        quotas["KR"].Should().BeLessThanOrEqualTo(2 * quotas["EUW1"]);
    }

    [Fact]
    public void Allocate_TapersTheAdvantage_AsThePlatformCatchesUp()
    {
        // Self-damping: the same signal that hands KR extra budget shrinks as that budget
        // does its work, so the allocation converges on an even split rather than oscillating.
        var starved = PlatformBudgetAllocator.Allocate(Platforms, 75, Snapshot(kr: 1, euw: 20, na: 20));
        var recovering = PlatformBudgetAllocator.Allocate(Platforms, 75, Snapshot(kr: 14, euw: 20, na: 20));

        recovering["KR"].Should().BeLessThan(starved["KR"]);
        recovering["KR"].Should().BeGreaterThan(25);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(50)]
    [InlineData(7500)]
    public void Allocate_AlwaysSpendsExactlyTheBudget(int budget)
    {
        // Largest remainder, not three independent floors: a batch that quietly lost slots to
        // rounding would under-spend the Riot budget on every single run.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, budget, Snapshot(kr: 1, euw: 20, na: 9));

        quotas.Values.Sum().Should().Be(budget);
    }

    [Fact]
    public void Allocate_GivesEveryPlatformASlot_WhenTheBudgetAllows()
    {
        // A zero slot means a region sits out the run — which is the failure this allocator
        // exists to prevent, so it must not be reachable through rounding.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, 3, Snapshot(kr: 0, euw: 20, na: 20));

        quotas.Values.Should().AllSatisfy(slots => slots.Should().BeGreaterThan(0));
    }

    [Fact]
    public void Allocate_DoesNotInventSlots_WhenTheBudgetIsSmallerThanThePlatformCount()
    {
        // Nothing to guarantee here: with fewer slots than platforms someone must sit out,
        // and inflating the budget to avoid that would over-spend it.
        var quotas = PlatformBudgetAllocator.Allocate(Platforms, 1, Snapshot(kr: 0, euw: 20, na: 20));

        quotas.Values.Sum().Should().Be(1);
    }

    [Fact]
    public void Allocate_IgnoresBlanksAndDuplicates()
    {
        var quotas = PlatformBudgetAllocator.Allocate(
            ["KR", " ", "kr", "EUW1", ""],
            10,
            ChampionCoverageSnapshot.Empty);

        quotas.Should().HaveCount(2);
        quotas.Values.Sum().Should().Be(10);
    }

    [Fact]
    public void Allocate_ReturnsEmpty_ForNoUsablePlatform()
    {
        PlatformBudgetAllocator.Allocate([" ", ""], 10, ChampionCoverageSnapshot.Empty)
            .Should().BeEmpty();
    }

    /// <summary>
    /// A snapshot over a fixed 10-champion pool where each platform holds the same number of
    /// mains on every champion — so <c>MeanDeficit</c> is exactly the per-champion deficit and
    /// the arithmetic above stays readable.
    /// </summary>
    private static ChampionCoverageSnapshot Snapshot(int kr, int euw, int na)
    {
        var mains = new Dictionary<(string, int), int>();
        foreach (var championId in Enumerable.Range(1, 10))
        {
            Add("KR", kr);
            Add("EUW1", euw);
            Add("NA1", na);

            void Add(string platform, int count)
            {
                // Absent rather than 0: that is how the grouped IsMain query reports "no mains
                // for this pair", and the snapshot must read the absence as a full deficit.
                if (count > 0)
                {
                    mains[(platform, championId)] = count;
                }
            }
        }

        return new ChampionCoverageSnapshot(mains, targetMainsPerChampion: 20);
    }
}
