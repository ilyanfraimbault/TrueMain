using TrueMain.Authentication;

namespace TrueMain.UnitTests;

/// <summary>The comparison both API-key schemes rely on.</summary>
public class ApiKeyComparisonTests
{
    private const string Configured = "the-configured-key-of-forty-characters!!";

    [Fact]
    public void MatchesTheConfiguredKey() => Assert.True(ApiKeyComparison.Matches(Configured, Configured));

    [Theory]
    [InlineData("the-configured-key-of-forty-characters!?")]
    [InlineData("the-configured-key")]
    [InlineData("")]
    public void RejectsAnyOtherValue(string provided) => Assert.False(ApiKeyComparison.Matches(provided, Configured));
}
