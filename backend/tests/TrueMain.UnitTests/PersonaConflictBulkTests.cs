using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Personas;

namespace TrueMain.UnitTests;

/// <summary>
/// Which conflicts a bulk resolution may merge (#2295), and into which known player: a shared social
/// channel, no differing one, the floor reached; kept is the older of the two.
/// </summary>
public sealed class PersonaConflictBulkTests
{
    [Theory]
    [InlineData(2, 0, 0, 20, true)]
    [InlineData(1, 0, 0, 20, false)]
    [InlineData(1, 4, 0, 20, true)]
    [InlineData(2, 0, 1, 10, false)]
    [InlineData(0, 4, 0, 10, false)]
    public void IsEligible_NeedsASharedChannel_NoDifferingOne_AndTheFloor(
        int sharedSocial, int sharedReferences, int differing, int floor, bool expected)
    {
        var evidence = new PersonaConflictEvidence(
            Enumerable.Repeat(PersonaLinkKind.Twitch, sharedSocial).ToList(),
            sharedReferences,
            Enumerable.Repeat(PersonaLinkKind.YouTube, differing).ToList());

        PersonaConflictBulk.IsEligible(evidence, floor).Should().Be(expected);
    }

    [Fact]
    public void KeepClaimant_KeepsTheOlder_AndBreaksTiesOnTheId()
    {
        var older = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = older.AddDays(1);
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("ffffffff-0000-0000-0000-000000000000");

        PersonaConflictBulk.KeepClaimant(older, high, newer, low).Should().BeTrue();
        PersonaConflictBulk.KeepClaimant(newer, low, older, high).Should().BeFalse();
        PersonaConflictBulk.KeepClaimant(older, low, older, high).Should().BeTrue();
        PersonaConflictBulk.KeepClaimant(older, high, older, low).Should().BeFalse();
    }
}
