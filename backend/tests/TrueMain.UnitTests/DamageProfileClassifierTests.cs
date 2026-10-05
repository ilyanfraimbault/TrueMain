using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Data.Statics;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Profiles;

namespace TrueMain.UnitTests;

/// <summary>
/// The rules of the damage-profile endpoint (#1905): shares that sum to 1, the build split
/// and its floor, the flex flag, and a static fallback that names a class and never a share.
/// </summary>
public sealed class DamageProfileClassifierTests
{
    [Fact]
    public void Measured_CarriesSharesThatSumToOne_AndTheDamageWeight()
    {
        var profile = DamageProfileClassifier.Measured(Facts(physical: 0.7, magic: 0.2, damagePerGame: 20_000), "MIDDLE", [])!;

        profile.Source.Should().Be(ChampionDamageProfileSources.Measured);
        profile.Position.Should().Be("MIDDLE");
        profile.Patch.Should().Be("16.19");
        (profile.PhysicalShare + profile.MagicShare + profile.TrueShare).Should().BeApproximately(1, 1e-9);
        profile.TrueShare.Should().BeApproximately(0.1, 1e-9);
        profile.DamagePerGame.Should().Be(20_000);
        profile.DamageClass.Should().BeNull("a measured entry's shares say more than a class");
    }

    [Fact]
    public void Measured_DropsAProfileWithNoDamage()
        => DamageProfileClassifier.Measured(Facts(physical: 0, magic: 0, damagePerGame: 0), "TOP", []).Should().BeNull();

    [Fact]
    public void Builds_KeepsTheArchetypesAboveTheFloor_AndFlagsAChampionThatGoesBothWays()
    {
        // Kai'Sa: 600 on-hit games, mostly physical; 300 AP games, mostly magic; 50 unclassified.
        var builds = DamageProfileClassifier.Builds(
        [
            Row(ItemArchetype.OnHit, 600, physical: 15_000, magic: 4_000),
            Row(ItemArchetype.AbilityPower, 300, physical: 5_000, magic: 14_000),
            Row(ItemArchetype.None, 50, physical: 10_000, magic: 10_000),
        ]);

        builds.Select(build => build.Archetype).Should().Equal("OnHit", "AbilityPower");
        builds[0].Share.Should().BeApproximately(600d / 950, 1e-9, "the share counts every archetype game, kept or not");
        builds.Should().OnlyContain(build =>
            Math.Abs(build.PhysicalShare + build.MagicShare + build.TrueShare - 1) < 1e-9);
        DamageProfileClassifier.IsFlex(builds).Should().BeTrue();
    }

    [Fact]
    public void IsFlex_IsFalse_ForTwoBuildsOfTheSameDamageType_OrAMarginalOffBuild()
    {
        // Varus on-hit vs lethality: two builds, both physical.
        DamageProfileClassifier.IsFlex(DamageProfileClassifier.Builds(
        [
            Row(ItemArchetype.OnHit, 500, physical: 15_000, magic: 5_000),
            Row(ItemArchetype.ArmorPenetration, 500, physical: 18_000, magic: 1_000),
        ])).Should().BeFalse();

        // An AP build on 10% of the games is the odd game, not a second way to play.
        DamageProfileClassifier.IsFlex(DamageProfileClassifier.Builds(
        [
            Row(ItemArchetype.Crit, 1_800, physical: 15_000, magic: 1_000),
            Row(ItemArchetype.AbilityPower, 200, physical: 3_000, magic: 14_000),
        ])).Should().BeFalse();
    }

    [Theory]
    [InlineData(2, 9, "magic")]
    [InlineData(9, 1, "physical")]
    [InlineData(7, 6, "mixed")]
    public void FallbackClass_NamesADamageTypeOnlyWhenTheRatingsAreFarApart(int attack, int magic, string expected)
        => DamageProfileClassifier.FallbackClass(new ChampionStatics(1, "X", 500, attack, magic)).Should().Be(expected);

    [Fact]
    public void FallbackClass_IsNull_WhenDataDragonDoesNotRateTheChampion()
        => DamageProfileClassifier.FallbackClass(new ChampionStatics(1, "X", 500)).Should().BeNull();

    private static ChampionProfileFacts Facts(double physical, double magic, double damagePerGame)
        => new()
        {
            ChampionId = 145,
            Position = "BOTTOM",
            Patch = "16.19",
            Games = 1_000,
            DamagePerGame = damagePerGame,
            PhysicalShare = physical,
            MagicShare = magic,
            SustainPerMinute = 0,
            CrowdControlPerMinute = 0,
        };

    private static ChampionDamageProfileStat Row(ItemArchetype archetype, int games, long physical, long magic)
        => new()
        {
            ChampionId = 145,
            Position = "BOTTOM",
            Patch = "16.19",
            Archetype = archetype,
            Games = games,
            PhysicalDamageToChampionsSum = games * physical,
            MagicDamageToChampionsSum = games * magic,
            TrueDamageToChampionsSum = games * 500L,
        };
}
