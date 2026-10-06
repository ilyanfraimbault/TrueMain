using Core.Lol.Identifiers;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class PlatformIdTests
{
    [Fact]
    public void Default_ThrowsOnRouteAccess()
    {
        var act = () => _ = default(PlatformId).Route;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Default_ThrowsOnImplicitStringConversion()
    {
        var act = () => _ = (string)default(PlatformId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("EUW1", PlatformRoute.EUW1)]
    [InlineData("euw1", PlatformRoute.EUW1)]
    [InlineData("  KR  ", PlatformRoute.KR)]
    public void TryParse_TrimsAndIsCaseInsensitive(string input, PlatformRoute expected)
    {
        var parsed = PlatformId.TryParse(input, out var platformId);

        parsed.Should().BeTrue();
        platformId.Route.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("12345")]
    public void TryParse_RejectsNumericStrings(string input)
    {
        var parsed = PlatformId.TryParse(input, out _);

        parsed.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZZZ9")]
    public void TryParse_RejectsInvalid(string? input)
    {
        var parsed = PlatformId.TryParse(input, out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void Default_DoesNotEqualBR1()
    {
        // The +1 backing-field encoding exists so default(PlatformId) stays
        // structurally distinct from a real BR1 (the zero-value route).
        var br1 = new PlatformId(PlatformRoute.BR1);

        default(PlatformId).Should().NotBe(br1);
    }

    [Theory]
    [InlineData(PlatformRoute.BR1, "BR1")]   // zero-value route — the tricky case
    [InlineData(PlatformRoute.EUW1, "EUW1")]
    [InlineData(PlatformRoute.KR, "KR")]
    public void ValueAndToStringReturnTheCanonicalRouteName(PlatformRoute route, string expected)
    {
        var platformId = new PlatformId(route);

        platformId.Value.Should().Be(expected);
        platformId.ToString().Should().Be(expected);
    }
}
