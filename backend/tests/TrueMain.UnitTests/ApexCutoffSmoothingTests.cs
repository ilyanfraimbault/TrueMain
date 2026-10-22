using AwesomeAssertions;
using Core.Lol.Ranking;
using TrueMain.Services.Ladder;
using Row = TrueMain.Services.Ladder.ApexCutoffsQueryService.CutoffRow;

namespace TrueMain.UnitTests;

/// <summary>
/// The read-time smoothing of estimated apex cutoffs (#2181): an estimated day takes the median
/// of itself and its neighbours, a measured day is served as read.
/// </summary>
public sealed class ApexCutoffSmoothingTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    [Fact]
    public void AnEstimatedOutlierTakesTheMedianOfItsNeighbours()
    {
        var smoothed = ApexCutoffsQueryService.Smoothed(
        [
            Estimated(-1, 410),
            Estimated(0, 220),
            Estimated(1, 420),
        ]);

        smoothed[Day].Should().Be(410);
        smoothed[Day.AddDays(-1)].Should().Be(220, "at the window's edge the lower middle wins: an estimate errs high");
        smoothed[Day.AddDays(1)].Should().Be(220);
    }

    [Fact]
    public void AMeasuredDayIsServedAsRead()
    {
        var smoothed = ApexCutoffsQueryService.Smoothed(
        [
            Estimated(-1, 500),
            new Row(Day, RankTier.Grandmaster, 300, IsEstimated: false),
            Estimated(1, 500),
        ]);

        smoothed[Day].Should().Be(300);
    }

    [Fact]
    public void ALoneEstimateKeepsItsValue()
    {
        ApexCutoffsQueryService.Smoothed([Estimated(0, 433)])[Day].Should().Be(433);
    }

    private static Row Estimated(int offset, int leaguePoints)
        => new(Day.AddDays(offset), RankTier.Grandmaster, leaguePoints, IsEstimated: true);
}
