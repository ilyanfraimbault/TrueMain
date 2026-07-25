using AwesomeAssertions;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

public sealed class NameTagParserTests
{
    [Theory]
    [InlineData("Phantasm-EUW1", "Phantasm", "EUW1")]
    [InlineData("GXI Flakked-EUW", "GXI Flakked", "EUW")]
    [InlineData("Some-Player-Name-NA1", "Some-Player-Name", "NA1")]
    [InlineData("a-b", "a", "b")]
    public void TryParse_splits_on_last_hyphen(string input, string expectedGameName, string expectedTagLine)
    {
        var parsed = NameTagParser.TryParse(input, out var result);

        parsed.Should().BeTrue();
        result.GameName.Should().Be(expectedGameName);
        result.TagLine.Should().Be(expectedTagLine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSeparator")]
    [InlineData("-LeadingHyphen")]
    [InlineData("TrailingHyphen-")]
    public void TryParse_returns_false_on_invalid(string? input)
    {
        var parsed = NameTagParser.TryParse(input, out var result);

        parsed.Should().BeFalse();
        result.Should().Be(default);
    }

    [Theory]
    [InlineData("Phantasm#EUW1", "Phantasm", "EUW1")]
    [InlineData("  Phantasm#EUW1  ", "Phantasm", "EUW1")]
    [InlineData("GXI Flakked #EUW", "GXI Flakked", "EUW")]
    [InlineData("Some-Player-Name#NA1", "Some-Player-Name", "NA1")]
    // No '#' at all falls back to the URL slug form.
    [InlineData("Some-Player-Name-NA1", "Some-Player-Name", "NA1")]
    public void TryParseRiotId_accepts_the_typed_and_slug_forms(
        string input,
        string expectedGameName,
        string expectedTagLine)
    {
        var parsed = NameTagParser.TryParseRiotId(input, out var result);

        parsed.Should().BeTrue();
        result.GameName.Should().Be(expectedGameName);
        result.TagLine.Should().Be(expectedTagLine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSeparator")]
    [InlineData("#EUW1")]
    [InlineData("Phantasm#")]
    [InlineData("Phantasm#EUW#1")]
    public void TryParseRiotId_returns_false_on_invalid(string? input)
    {
        var parsed = NameTagParser.TryParseRiotId(input, out var result);

        parsed.Should().BeFalse();
        result.Should().Be(default);
    }
}
