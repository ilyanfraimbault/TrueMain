using FluentAssertions;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

public sealed class RegionFilterParserTests
{
    [Fact]
    public void Parse_europe_returns_the_european_shards()
    {
        RegionFilterParser.Parse("europe")
            .Should().BeEquivalentTo(["EUW1", "EUN1", "RU", "TR1"]);
    }

    [Fact]
    public void Parse_americas_returns_the_american_shards()
    {
        RegionFilterParser.Parse("americas")
            .Should().BeEquivalentTo(["NA1", "BR1", "LA1", "LA2", "OC1"]);
    }

    [Fact]
    public void Parse_korea_returns_only_KR_not_JP1()
    {
        // Riot groups KR + JP1 under RegionalRoute.Asia but the leaderboard
        // surfaces Korea as its own pill — JP1 is intentionally excluded in
        // V1 to keep the filter label honest.
        RegionFilterParser.Parse("korea")
            .Should().BeEquivalentTo(["KR"]);
    }

    [Theory]
    [InlineData("EUROPE")]
    [InlineData("Europe")]
    [InlineData("  europe  ")]
    [InlineData("KOREA")]
    public void Parse_is_case_insensitive_and_trims_whitespace(string input)
    {
        RegionFilterParser.Parse(input).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("asia")] // intentionally not exposed in V1
    [InlineData("sea")] // intentionally not exposed in V1
    [InlineData("not-a-region")]
    public void Parse_returns_null_for_missing_or_unknown_slug(string? input)
    {
        RegionFilterParser.Parse(input).Should().BeNull();
    }

    [Fact]
    public void AllExposedPlatforms_is_union_of_the_three_pills_without_JP1_or_SEA()
    {
        var platforms = RegionFilterParser.AllExposedPlatforms();

        platforms.Should().Contain(["EUW1", "EUN1", "RU", "TR1", "NA1", "BR1", "LA1", "LA2", "OC1", "KR"]);
        platforms.Should().NotContain("JP1");
        platforms.Should().NotContain("PH2");
        platforms.Should().NotContain("SG2");
        platforms.Should().NotContain("TH2");
        platforms.Should().NotContain("TW2");
        platforms.Should().NotContain("VN2");
    }

    [Theory]
    [InlineData("EUW1", "europe")]
    [InlineData("EUN1", "europe")]
    [InlineData("RU", "europe")]
    [InlineData("TR1", "europe")]
    [InlineData("NA1", "americas")]
    [InlineData("BR1", "americas")]
    [InlineData("LA1", "americas")]
    [InlineData("OC1", "americas")]
    [InlineData("KR", "korea")]
    public void RouteToSlug_maps_exposed_platforms_to_their_pill(string platform, string expectedSlug)
    {
        RegionFilterParser.RouteToSlug(platform).Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData("JP1")] // grouped under Asia but the Korea pill excludes it
    [InlineData("PH2")] // SEA — not exposed
    [InlineData("VN2")] // SEA — not exposed
    [InlineData("not-a-platform")]
    public void RouteToSlug_returns_null_for_unexposed_or_unknown_platform(string platform)
    {
        RegionFilterParser.RouteToSlug(platform).Should().BeNull();
    }
}
