using AwesomeAssertions;
using Ingestor.Processes.Components.LadderSync;

namespace TrueMain.UnitTests;

/// <summary>
/// The slot ordering and cursor arithmetic of the paginated ladder sweep (#1312). An
/// off-by-one here does not fail loudly — it silently skips a division on every sweep — so
/// the ordering is pinned rather than left to the reader.
/// </summary>
public sealed class LadderSweepPlanTests
{
    [Fact]
    public void BuildSlots_OrdersTiersHighestFirstAndDivisionsOneFirst()
    {
        var slots = LadderSweepPlan.BuildSlots(["Emerald", "Diamond"]);

        slots.Select(slot => $"{slot.Tier} {slot.Division}").Should().Equal(
            "DIAMOND I", "DIAMOND II", "DIAMOND III", "DIAMOND IV",
            "EMERALD I", "EMERALD II", "EMERALD III", "EMERALD IV");
    }

    [Fact]
    public void BuildSlots_DropsApexTiers()
    {
        // Master, Grandmaster and Challenger have a whole-ladder endpoint each and no
        // divisions to page through, so they must never enter the paginated sweep.
        var slots = LadderSweepPlan.BuildSlots(["Challenger", "GM", "Master", "Diamond"]);

        slots.Select(slot => slot.Tier).Distinct().Should().Equal("DIAMOND");
    }

    [Fact]
    public void BuildSlots_IgnoresUnknownTiers()
    {
        LadderSweepPlan.BuildSlots(["Diamond", "Mythic", ""]).Should().HaveCount(4);
    }

    [Fact]
    public void ApexTiersInScope_ReturnsOnlyApexTiersHighestFirst_AndAcceptsTheGmShorthand()
    {
        LadderSweepPlan.ApexTiersInScope(["Master", "Diamond", "GM", "Challenger"])
            .Should().Equal("CHALLENGER", "GRANDMASTER", "MASTER");
    }

    [Fact]
    public void IndexOfOrStart_FindsTheSlot()
    {
        var slots = LadderSweepPlan.BuildSlots(["Diamond", "Emerald"]);

        LadderSweepPlan.IndexOfOrStart(slots, new LadderSweepSlot("EMERALD", "III")).Should().Be(6);
    }

    [Fact]
    public void IndexOfOrStart_RestartsAtTheTopWhenTheStoredSlotLeftTheScope()
    {
        // A cursor written while Platinum was in scope must not be read as an offset once
        // Platinum is dropped — that would land the sweep on an arbitrary division.
        var slots = LadderSweepPlan.BuildSlots(["Diamond"]);

        LadderSweepPlan.IndexOfOrStart(slots, new LadderSweepSlot("PLATINUM", "II")).Should().Be(0);
        LadderSweepPlan.IndexOfOrStart(slots, null).Should().Be(0);
    }
}
