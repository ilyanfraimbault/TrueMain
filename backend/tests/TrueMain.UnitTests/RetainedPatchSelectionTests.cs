using AwesomeAssertions;
using Ingestor.Processes;

namespace TrueMain.UnitTests;

/// <summary>
/// #1233 / ING-7: retention now derives its retained patches from grouped rows instead
/// of the whole match list, so the selection has to order patches itself. These pin
/// that the outcome is the one the per-match descending scan used to produce — the
/// newest patches per platform, normalised to major.minor.
/// </summary>
public sealed class RetainedPatchSelectionTests
{
    [Fact]
    public void Keeps_the_newest_patches_per_platform()
    {
        var observed = new List<MatchDataRetentionProcess.ObservedPatch>
        {
            new("EUW1", "14.1.500.1234", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
            new("EUW1", "14.3.700.9999", new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)),
            new("EUW1", "14.2.600.4321", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
            new("KR", "14.2.600.4321", new DateTime(2026, 2, 11, 0, 0, 0, DateTimeKind.Utc)),
        };

        var retained = MatchDataRetentionProcess.ComputeRetainedPatchesByPlatform(observed, retainedPatchCount: 2);

        retained["EUW1"].Should().BeEquivalentTo(["14.3", "14.2"]);
        retained["KR"].Should().BeEquivalentTo(["14.2"]);
    }

    [Fact]
    public void Collapses_the_build_numbers_of_a_single_patch_into_one_retained_slot()
    {
        // Two game versions normalising to 14.3 are one patch, not two: the retention
        // count is a patch count, and counting builds would silently halve the history.
        var observed = new List<MatchDataRetentionProcess.ObservedPatch>
        {
            new("EUW1", "14.3.700.9999", new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)),
            new("EUW1", "14.3.701.1111", new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
            new("EUW1", "14.2.600.4321", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
        };

        var retained = MatchDataRetentionProcess.ComputeRetainedPatchesByPlatform(observed, retainedPatchCount: 2);

        retained["EUW1"].Should().BeEquivalentTo(["14.3", "14.2"]);
    }

    [Fact]
    public void Drops_unparseable_game_versions_instead_of_spending_a_retained_slot_on_them()
    {
        var observed = new List<MatchDataRetentionProcess.ObservedPatch>
        {
            new("EUW1", "not-a-version", new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc)),
            new("EUW1", "14.2.600.4321", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
        };

        var retained = MatchDataRetentionProcess.ComputeRetainedPatchesByPlatform(observed, retainedPatchCount: 1);

        retained["EUW1"].Should().BeEquivalentTo(["14.2"]);
    }
}
