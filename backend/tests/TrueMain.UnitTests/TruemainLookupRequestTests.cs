using AwesomeAssertions;
using TrueMain.Services.Truemains.Lookup;

namespace TrueMain.UnitTests;

public sealed class TruemainLookupRequestTests
{
    [Fact]
    public void Parses_lowers_dedupes_and_sorts_the_pairs()
    {
        var parsed = TruemainLookupRequest.TryParse(
            " euw1 ",
            ["Zed Lord#EUW:238", "ahri fan#Fr1:103", "AHRI FAN#fr1:103"],
            out var request,
            out _);

        parsed.Should().BeTrue();
        request.PlatformId.Should().Be("EUW1");
        request.Pairs.Should().Equal(
            new TruemainLookupPair("ahri fan", "fr1", 103),
            new TruemainLookupPair("zed lord", "euw", 238));
    }

    [Fact]
    public void The_same_game_in_another_order_or_casing_shares_its_key()
    {
        TruemainLookupRequest.TryParse("EUW1", ["A#1:1", "B#2:2"], out var first, out _);
        TruemainLookupRequest.TryParse("euw1", ["b#2:2", "a#1:1"], out var second, out _);

        second.Key.Should().Be(first.Key);
    }

    [Fact]
    public void Splits_the_champion_on_the_last_colon()
    {
        TruemainLookupRequest.TryParse("NA1", ["Re:Zero#NA1:99"], out var request, out _).Should().BeTrue();

        request.Pairs.Should().ContainSingle().Which.Should().Be(new TruemainLookupPair("re:zero", "na1", 99));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EUW 1")]
    [InlineData("EUW1EUW1X")]
    public void Rejects_a_malformed_platform(string? platformId)
    {
        TruemainLookupRequest.TryParse(platformId, ["A#1:1"], out _, out var error).Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("NoTag:103")]
    [InlineData("Name#TAG")]
    [InlineData("Name#TAG:")]
    [InlineData("Name#TAG:0")]
    [InlineData("Name#TAG:-5")]
    [InlineData("Name#TAG:+5")]
    [InlineData("Name#TAG:1x")]
    [InlineData("#TAG:103")]
    [InlineData(":103")]
    public void Rejects_a_malformed_player(string player)
    {
        TruemainLookupRequest.TryParse("EUW1", [player], out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Rejects_no_player_and_more_than_ten()
    {
        TruemainLookupRequest.TryParse("EUW1", [], out _, out _).Should().BeFalse();
        TruemainLookupRequest.TryParse("EUW1", null, out _, out _).Should().BeFalse();

        var eleven = Enumerable.Range(1, 11).Select(i => $"P{i}#EUW:{i}").ToArray();
        TruemainLookupRequest.TryParse("EUW1", eleven, out _, out _).Should().BeFalse();
        TruemainLookupRequest.TryParse("EUW1", eleven[..10], out _, out _).Should().BeTrue();
    }
}
