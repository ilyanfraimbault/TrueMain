using AwesomeAssertions;
using TrueMain.Services.Champions.Directory;
using static TrueMain.Services.Champions.Directory.ChampionDirectoryTopBuilds;

namespace TrueMain.UnitTests;

public sealed class ChampionDirectoryTopBuildsTests
{
    private const int Ahri = 103;
    private const int Luden = 6655;
    private const int Rocket = 3152;
    private const int Electrocute = 8112;
    private const int Conqueror = 8010;

    private static readonly Guid LudenBuild = Guid.NewGuid();
    private static readonly Guid RocketBuild = Guid.NewGuid();
    private static readonly Guid BrokenBuild = Guid.NewGuid();
    private static readonly Guid ElectrocutePage = Guid.NewGuid();
    private static readonly Guid ConquerorPage = Guid.NewGuid();

    private static readonly Dictionary<Guid, BuildItems> Builds = new()
    {
        [LudenBuild] = new BuildItems(Luden, 3020, 4645, 0, 0, 0, 0),
        [RocketBuild] = new BuildItems(Rocket, 3020, 3157, 0, 0, 0, 0),
        [BrokenBuild] = new BuildItems(0, 3020, 3157, 0, 0, 0, 0),
    };

    private static readonly Dictionary<Guid, RuneChoice> Runes = new()
    {
        [ElectrocutePage] = new RuneChoice(Electrocute, 8300),
        [ConquerorPage] = new RuneChoice(Conqueror, 8400),
    };

    [Fact]
    public void GamesAddUpAcrossTheBandsTheFilterSelects()
    {
        var subject = Subject(
            Row("GOLD", true, RocketBuild, ConquerorPage, games: 60),
            Row("MASTER", true, LudenBuild, ElectrocutePage, games: 40),
            Row("CHALLENGER", true, LudenBuild, ElectrocutePage, games: 30));

        // Across every band Luden has 70 games to Rocket's 60.
        subject.Resolve(null, truemainsOnly: true)[(Ahri, "MIDDLE")].FirstItemId.Should().Be(Luden);

        // Below Master, Rocket is all there is.
        subject.Resolve(["GOLD"], truemainsOnly: true)[(Ahri, "MIDDLE")].FirstItemId.Should().Be(Rocket);
    }

    [Fact]
    public void AnEmptyFilterMatchesNothingRatherThanWideningToEveryBand()
    {
        var subject = Subject(Row("GOLD", true, LudenBuild, ElectrocutePage, games: 60));

        subject.Resolve([], truemainsOnly: true).Should().BeEmpty();
    }

    [Fact]
    public void ThePopulationSelectsMainsOrEveryTrackedPlayer()
    {
        var subject = Subject(
            Row("GOLD", isMain: true, LudenBuild, ElectrocutePage, games: 10),
            Row("GOLD", isMain: false, RocketBuild, ConquerorPage, games: 90));

        subject.Resolve(null, truemainsOnly: true)[(Ahri, "MIDDLE")].FirstItemId.Should().Be(Luden);
        subject.Resolve(null, truemainsOnly: false)[(Ahri, "MIDDLE")].FirstItemId.Should().Be(Rocket);
    }

    [Fact]
    public void ATieGoesToTheLowerFirstItem()
    {
        var subject = Subject(
            Row("GOLD", true, LudenBuild, ElectrocutePage, games: 50),
            Row("GOLD", true, RocketBuild, ElectrocutePage, games: 50));

        subject.Resolve(null, truemainsOnly: true)[(Ahri, "MIDDLE")].FirstItemId.Should().Be(Rocket);
    }

    [Fact]
    public void ARowWithAMalformedOrMissingDimensionCountsForNothing()
    {
        var subject = Subject(
            Row("GOLD", true, BrokenBuild, ElectrocutePage, games: 90),
            Row("GOLD", true, Guid.NewGuid(), ElectrocutePage, games: 90),
            Row("GOLD", true, LudenBuild, ElectrocutePage, games: 10));

        subject.Resolve(null, truemainsOnly: true)[(Ahri, "MIDDLE")].FirstItemId.Should().Be(Luden);
    }

    [Fact]
    public void TheBuildNamesTheBucketItsKeystoneAndItsDominantSecondaryTree()
    {
        var top = Subject(
            Row("GOLD", true, LudenBuild, ElectrocutePage, games: 80),
            Row("GOLD", true, LudenBuild, ConquerorPage, games: 20))
            .Resolve(null, truemainsOnly: true)[(Ahri, "MIDDLE")];

        top.PrimaryKeystoneId.Should().Be(Electrocute);
        top.SecondaryStyleId.Should().Be(8300);
        top.ItemPath.Should().StartWith([Luden]);
    }

    private static ChampionDirectoryTopBuilds Subject(params (string Band, bool IsMain, Guid BuildId, Guid RunePageId, int Games)[] rows)
    {
        var builder = new Builder();
        foreach (var (band, isMain, buildId, runePageId, games) in rows)
        {
            builder.Add(Ahri, "MIDDLE", band, isMain, buildId, runePageId, games, games / 2);
        }

        return new ChampionDirectoryTopBuilds(builder, Builds, Runes);
    }

    private static (string Band, bool IsMain, Guid BuildId, Guid RunePageId, int Games) Row(
        string band, bool isMain, Guid buildId, Guid runePageId, int games)
        => (band, isMain, buildId, runePageId, games);
}
