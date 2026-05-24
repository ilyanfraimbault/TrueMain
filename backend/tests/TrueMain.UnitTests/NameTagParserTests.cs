using FluentAssertions;
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
}
