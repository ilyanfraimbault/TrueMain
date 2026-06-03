using Core.Lol.Identifiers;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class PlatformIdTests
{
    [Fact]
    public void Default_throws_on_route_access()
    {
        var act = () => _ = default(PlatformId).Route;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Default_throws_on_implicit_string_conversion()
    {
        var act = () => _ = (string)default(PlatformId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("EUW1", PlatformRoute.EUW1)]
    [InlineData("euw1", PlatformRoute.EUW1)]
    [InlineData("  KR  ", PlatformRoute.KR)]
    public void TryParse_trims_and_is_case_insensitive(string input, PlatformRoute expected)
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
    public void TryParse_rejects_numeric_strings(string input)
    {
        var parsed = PlatformId.TryParse(input, out _);

        parsed.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZZZ9")]
    public void TryParse_rejects_invalid(string? input)
    {
        var parsed = PlatformId.TryParse(input, out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void Default_does_not_equal_BR1()
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
    public void Value_and_ToString_return_the_canonical_route_name(PlatformRoute route, string expected)
    {
        var platformId = new PlatformId(route);

        platformId.Value.Should().Be(expected);
        platformId.ToString().Should().Be(expected);
    }
}
