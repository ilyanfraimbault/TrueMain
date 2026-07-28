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
}
