using Core.Lol.Patches;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class PatchVersionTests
{
    [Theory]
    [InlineData("16.4", 16, 4, null)]
    [InlineData("16.4.521", 16, 4, 521)]
    [InlineData("16.4.521.123", 16, 4, 521)]
    [InlineData("16.4.x", 16, 4, null)]
    [InlineData(" 16 . 4 ", 16, 4, null)]
    [InlineData(" 16 . 4 . 521 ", 16, 4, 521)]
    public void Parse_returns_major_minor_build(string input, int major, int minor, int? build)
    {
        var version = PatchVersion.Parse(input);

        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Build.Should().Be(build);
        version.ToString().Should().Be(build is null ? $"{major}.{minor}" : $"{major}.{minor}.{build}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("16")]
    [InlineData("abc.def")]
    [InlineData("16.x")]
    public void Parse_throws_on_invalid(string? input)
    {
        var act = () => PatchVersion.Parse(input!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("16")]
    [InlineData("abc.def")]
    public void TryParse_returns_false_on_invalid(string? input)
    {
        var parsed = PatchVersion.TryParse(input, out var version);

        parsed.Should().BeFalse();
        version.Should().Be(default);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("16", "16")]
    [InlineData("16.4", "16.4")]
    [InlineData("16.4.521", "16.4")]
    [InlineData("16.4.521.123", "16.4")]
    public void Normalize_mirrors_legacy_behaviour(string? input, string expected)
    {
        PatchVersion.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Comparison_orders_by_major_then_minor()
    {
        var a = new PatchVersion(15, 12);
        var b = new PatchVersion(16, 1);
        var c = new PatchVersion(16, 4);

        (a < b).Should().BeTrue();
        (b < c).Should().BeTrue();
        (a < c).Should().BeTrue();
        (c > a).Should().BeTrue();
        (b == new PatchVersion(16, 1)).Should().BeTrue();
        (c >= b).Should().BeTrue();
        (a <= b).Should().BeTrue();
    }

    [Fact]
    public void Comparison_orders_base_patch_before_its_hotfix()
    {
        var basePatch = new PatchVersion(16, 4);
        var hotfix = new PatchVersion(16, 4, 521);
        var laterHotfix = new PatchVersion(16, 4, 530);

        (basePatch < hotfix).Should().BeTrue();
        (hotfix < laterHotfix).Should().BeTrue();
        (basePatch < new PatchVersion(16, 5)).Should().BeTrue();
        (hotfix > basePatch).Should().BeTrue();
    }

    [Fact]
    public void Equality_distinguishes_build_segment()
    {
        var basePatch = new PatchVersion(16, 4);
        var hotfix = new PatchVersion(16, 4, 521);

        basePatch.Should().NotBe(hotfix);
        (basePatch == hotfix).Should().BeFalse();
        hotfix.Should().Be(new PatchVersion(16, 4, 521));
        (hotfix == new PatchVersion(16, 4, 521)).Should().BeTrue();
    }
}
