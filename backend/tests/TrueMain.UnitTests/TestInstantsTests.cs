using AwesomeAssertions;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

/// <summary>
/// The clamp behind the rank-snapshot fixtures. Its whole reason to exist is a branch that
/// is only reachable for one hour a day, so it is asserted here with an explicit clock
/// rather than left to whatever time CI happens to run at.
/// </summary>
public sealed class TestInstantsTests
{
    [Fact]
    public void SubtractsNormally_WhenTheResultIsStillToday()
    {
        var now = new DateTime(2026, 9, 4, 13, 30, 0, DateTimeKind.Utc);

        TestInstants.EarlierSameUtcDay(TimeSpan.FromHours(1), now)
            .Should().Be(new DateTime(2026, 9, 4, 12, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ClampsToTheStartOfTheDay_WhenSubtractingWouldCrossMidnight()
    {
        // 00:36 UTC minus an hour is yesterday — the exact case that failed CI.
        var now = new DateTime(2026, 9, 4, 0, 36, 0, DateTimeKind.Utc);

        var captured = TestInstants.EarlierSameUtcDay(TimeSpan.FromHours(1), now);

        captured.Should().Be(new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc));
        captured.Date.Should().Be(now.Date, "the point of the helper is that it stays the same UTC day");
        captured.Should().BeOnOrBefore(now, "and never in the future");
    }

    [Fact]
    public void IsExactlyMidnight_AtMidnight()
    {
        var now = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);

        TestInstants.EarlierSameUtcDay(TimeSpan.FromHours(1), now).Should().Be(now);
    }
}
