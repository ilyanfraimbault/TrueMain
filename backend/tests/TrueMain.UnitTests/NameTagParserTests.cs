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

    [Fact]
    public void TryParseRiotId_rejects_input_past_the_length_cap()
    {
        // The DB caps GameName at 32 and TagLine at 8, so anything past the cap
        // is junk or abuse. The cap lives here so the controller (which turns a
        // failure into a 400) and the query service can't disagree on it.
        var atCap = new string('a', NameTagParser.MaxRiotIdLength - 5) + "#EUW1";
        atCap.Length.Should().Be(NameTagParser.MaxRiotIdLength);
        NameTagParser.TryParseRiotId(atCap, out _).Should().BeTrue();

        var pastCap = new string('a', NameTagParser.MaxRiotIdLength - 4) + "#EUW1";
        NameTagParser.TryParseRiotId(pastCap, out var result).Should().BeFalse();
        result.Should().Be(default);
    }
}
