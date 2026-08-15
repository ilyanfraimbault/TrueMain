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
    public void Kda_divides_takedowns_by_deaths()
    {
        RateMath.Kda(kills: 10, deaths: 4, assists: 6, games: 3).Should().Be(4d);
    }

    [Fact]
    public void Kda_falls_back_to_a_per_game_figure_when_the_sample_never_died()
    {
        // The bug this pins (#871): the fallback used to return kills + assists
        // as a raw career sum, printing e.g. 150 next to per-game averages.
        // Ten deathless games with 100 kills and 50 assists is 15 per game.
        RateMath.Kda(kills: 100, deaths: 0, assists: 50, games: 10).Should().Be(15d);
    }

    [Fact]
    public void Kda_deathless_fallback_stays_on_the_same_scale_as_a_played_sample()
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
    public void Kda_returns_zero_for_an_empty_sample()
    {
        RateMath.Kda(kills: 0, deaths: 0, assists: 0, games: 0).Should().Be(0d);
    }

    [Fact]
    public void Rate_returns_zero_on_an_empty_denominator()
    {
        RateMath.Rate(3, 0).Should().Be(0d);
        RateMath.Rate(3, 12).Should().Be(0.25d);
    }

    [Fact]
    public void WinRate_is_null_when_a_counter_is_unknown_or_no_games_were_played()
    {
        RateMath.WinRate(null, 4).Should().BeNull();
        RateMath.WinRate(4, null).Should().BeNull();
        RateMath.WinRate(0, 0).Should().BeNull();
        RateMath.WinRate(3, 1).Should().Be(0.75d);
    }

    [Fact]
    public void WilsonInterval_brackets_the_observed_rate_and_stays_a_probability()
    {
        var (lower, upper) = RateMath.WilsonInterval(wins: 12, games: 20);

        lower.Should().BeLessThan(0.6d);
        upper.Should().BeGreaterThan(0.6d);
        lower.Should().BeGreaterThanOrEqualTo(0d);
        upper.Should().BeLessThanOrEqualTo(1d);
    }

    [Fact]
    public void WilsonInterval_stays_inside_zero_to_one_at_the_extremes()
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
    public void WilsonInterval_narrows_as_the_sample_grows()
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
    public void WilsonInterval_on_an_empty_sample_constrains_nothing()
    {
        RateMath.WilsonInterval(wins: 0, games: 0).Should().Be((0d, 1d));
    }
}
