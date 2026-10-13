using TrueMain.RequestLogging;

namespace TrueMain.UnitTests;

/// <summary>
/// A request-supplied value must not start a line of its own in the plain-text console log (#2057).
/// </summary>
public class LogValueTests
{
    [Theory]
    [InlineData("Faker\nINFO forged entry", "FakerINFO forged entry")]
    [InlineData("Faker\r\nINFO forged entry", "FakerINFO forged entry")]
    [InlineData("Faker\rINFO", "FakerINFO")]
    [InlineData("Faker#KR1", "Faker#KR1")]
    [InlineData("", "")]
    public void SingleLineDropsLineBreaks(string value, string expected)
        => Assert.Equal(expected, LogValue.SingleLine(value));

    [Fact]
    public void SingleLineKeepsNull() => Assert.Null(LogValue.SingleLine(null));
}
