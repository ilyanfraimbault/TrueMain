using AwesomeAssertions;
using TrueMain.Services;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the shared ratio arithmetic the read-model projections use. The KDA
/// cases matter most: the champion mains-comparison panel and the truemains
/// leaderboard both call <see cref="RateMath.Kda"/>, so a change here moves both
/// surfaces at once — which is the point (#871).
/// </summary>
public sealed class RateMathTests
{
    [Fact]
    public void Kda_DividesTakedownsByDeaths()
    {
        RateMath.Kda(kills: 10, deaths: 4, assists: 6, games: 3).Should().Be(4d);
    }

    [Fact]
    public void Kda_FallsBackToAPerGameFigureWhenTheSampleNeverDied()
    {
        // The bug this pins (#871): the fallback used to return kills + assists
        // as a raw career sum, printing e.g. 150 next to per-game averages.
        // Ten deathless games with 100 kills and 50 assists is 15 per game.
        RateMath.Kda(kills: 100, deaths: 0, assists: 50, games: 10).Should().Be(15d);
    }

    [Fact]
    public void Kda_DeathlessFallbackStaysOnTheSameScaleAsAPlayedSample()
    {
        // A deathless pool must not dwarf a merely excellent one by an order of
        // magnitude just because it is aggregated over many games.
        // Deathless: 150 takedowns over 10 games -> 15.
        // Compared against the same pool having died 15 times -> 10.
        var deathless = RateMath.Kda(kills: 100, deaths: 0, assists: 50, games: 10);
        var excellent = RateMath.Kda(kills: 100, deaths: 15, assists: 50, games: 10);

        deathless.Should().BeGreaterThan(excellent);
        deathless.Should().BeLessThan(excellent * 10d);
    }

    [Fact]
    public void Kda_ReturnsZeroForAnEmptySample()
    {
        RateMath.Kda(kills: 0, deaths: 0, assists: 0, games: 0).Should().Be(0d);
    }

    [Fact]
    public void Rate_ReturnsZeroOnAnEmptyDenominator()
    {
        RateMath.Rate(3, 0).Should().Be(0d);
        RateMath.Rate(3, 12).Should().Be(0.25d);
    }

    [Fact]
    public void RateOrNull_KeepsAnUnmeasuredSampleApartFromAMeasuredZero()
    {
        // The two conventions used to share one name (#1224): a private copy in the
        // live build aggregator returned 0 for an empty denominator while the
        // activity buckets returned null for the same case. Same arithmetic,
        // opposite meaning — so they are two named intents now.
        RateMath.RateOrNull(0, 0).Should().BeNull("no games is not a rate of zero");
        RateMath.RateOrNull(0, 12).Should().Be(0d, "twelve games and no win is a measured zero");
        RateMath.RateOrNull(3, 12).Should().Be(0.25d);
    }

    [Fact]
    public void WinRate_IsNullWhenACounterIsUnknownOrNoGamesWerePlayed()
    {
        RateMath.WinRate(null, 4).Should().BeNull();
        RateMath.WinRate(4, null).Should().BeNull();
        RateMath.WinRate(0, 0).Should().BeNull();
        RateMath.WinRate(3, 1).Should().Be(0.75d);
    }

    [Fact]
    public void WilsonInterval_BracketsTheObservedRateAndStaysAProbability()
    {
        var (lower, upper) = RateMath.WilsonInterval(wins: 12, games: 20);

        lower.Should().BeLessThan(0.6d);
        upper.Should().BeGreaterThan(0.6d);
        lower.Should().BeGreaterThanOrEqualTo(0d);
        upper.Should().BeLessThanOrEqualTo(1d);
    }

    [Fact]
    public void WilsonInterval_StaysInsideZeroToOneAtTheExtremes()
    {
        // The reason this is Wilson and not the textbook normal interval: a perfect
        // record puts the normal upper bound past 1.0, which is not a probability.
        var perfect = RateMath.WilsonInterval(wins: 9, games: 9);
        perfect.Upper.Should().BeLessThanOrEqualTo(1d);
        perfect.Lower.Should().BeGreaterThan(0d).And.BeLessThan(1d);

        var winless = RateMath.WilsonInterval(wins: 0, games: 9);
        winless.Lower.Should().BeGreaterThanOrEqualTo(0d);
        winless.Upper.Should().BeGreaterThan(0d);
    }

    [Fact]
    public void WilsonInterval_NarrowsAsTheSampleGrows()
    {
        // The whole point of ranking on the bound: the same rate measured on more
        // games claims more, so a thin sample cannot out-rank a thick one on
        // variance alone.
        var thin = RateMath.WilsonInterval(wins: 12, games: 20);
        var thick = RateMath.WilsonInterval(wins: 600, games: 1_000);

        (thick.Upper - thick.Lower).Should().BeLessThan(thin.Upper - thin.Lower);
        thick.Lower.Should().BeGreaterThan(thin.Lower, "same 60%, far more evidence for it");
    }

    [Fact]
    public void WilsonInterval_OnAnEmptySampleConstrainsNothing()
    {
        RateMath.WilsonInterval(wins: 0, games: 0).Should().Be((0d, 1d));
    }
}
