using AwesomeAssertions;
using TrueMain.Services.Champions.Directory;

namespace TrueMain.UnitTests;

public sealed class ChampionDirectoryScopeGroupsTests
{
    private const int Ahri = 103;
    private const int Jhin = 202;

    private static readonly DateTime Earlier = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 11, 3, 11, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid OtherMain = Guid.NewGuid();
    private static readonly Guid NotAMain = Guid.NewGuid();

    [Fact]
    public void GamesAndWinsAddUpAcrossTheBandsTheFilterSelects()
    {
        var subject = Subject(
            (Ahri, "MIDDLE", "GOLD", true, Main, 10, 6, Earlier),
            (Ahri, "MIDDLE", "MASTER", true, Main, 5, 3, Later),
            (Ahri, "MIDDLE", "CHALLENGER", true, OtherMain, 2, 1, Earlier));

        var all = subject.Resolve(null, truemainsOnly: true).Should().ContainSingle().Subject;
        all.Games.Should().Be(17);
        all.Wins.Should().Be(10);
        all.LastUpdatedAtUtc.Should().Be(Later);

        var masterPlus = subject.Resolve(["MASTER", "GRANDMASTER", "CHALLENGER"], truemainsOnly: true).Should().ContainSingle().Subject;
        masterPlus.Games.Should().Be(7);
        masterPlus.Wins.Should().Be(4);
    }

    [Fact]
    public void AnAccountInTwoBandsIsOneMain()
    {
        var subject = Subject(
            (Ahri, "MIDDLE", "GOLD", true, Main, 10, 6, Earlier),
            (Ahri, "MIDDLE", "MASTER", true, Main, 5, 3, Earlier),
            (Ahri, "MIDDLE", "MASTER", true, OtherMain, 5, 3, Earlier));

        subject.Resolve(null, truemainsOnly: true).Single().TrueMainCount.Should().Be(2);
    }

    [Fact]
    public void ThePopulationWidensTheGamesAndLeavesTheMainCountAlone()
    {
        var subject = Subject(
            (Ahri, "MIDDLE", "GOLD", true, Main, 10, 6, Earlier),
            (Ahri, "MIDDLE", "GOLD", false, NotAMain, 40, 20, Later));

        var mains = subject.Resolve(null, truemainsOnly: true).Single();
        mains.Games.Should().Be(10);
        mains.TrueMainCount.Should().Be(1);
        mains.LastUpdatedAtUtc.Should().Be(Earlier);

        var everyone = subject.Resolve(null, truemainsOnly: false).Single();
        everyone.Games.Should().Be(50);
        everyone.TrueMainCount.Should().Be(1);
        everyone.LastUpdatedAtUtc.Should().Be(Later);
    }

    [Fact]
    public void ALaneTheFilterSelectsNothingOnIsNoGroup()
    {
        var subject = Subject(
            (Ahri, "MIDDLE", "GOLD", true, Main, 10, 6, Earlier),
            (Jhin, "BOTTOM", "MASTER", true, Main, 5, 3, Earlier));

        subject.Resolve(["MASTER"], truemainsOnly: true).Single().ChampionId.Should().Be(Jhin);
    }

    [Fact]
    public void AnEmptyFilterMatchesNothingRatherThanWideningToEveryBand()
    {
        var subject = Subject((Ahri, "MIDDLE", "GOLD", true, Main, 10, 6, Earlier));

        subject.Resolve([], truemainsOnly: true).Should().BeEmpty();
    }

    [Fact]
    public void APositionlessScopeIsAGroupOfItsOwn()
    {
        var subject = Subject(
            (Ahri, "MIDDLE", "GOLD", true, Main, 10, 6, Earlier),
            (Ahri, string.Empty, "GOLD", true, Main, 3, 1, Earlier));

        subject.Resolve(null, truemainsOnly: true).Select(group => group.Position)
            .Should().BeEquivalentTo("MIDDLE", string.Empty);
    }

    private static ChampionDirectoryScopeGroups Subject(
        params (int ChampionId, string Position, string Band, bool IsMain, Guid Account, int Games, int Wins, DateTime At)[] rows)
    {
        var builder = new ChampionDirectoryScopeGroups.Builder();
        foreach (var row in rows)
        {
            builder.Add(row.ChampionId, row.Position, row.Band, row.IsMain, row.Account, row.Games, row.Wins, row.At);
        }

        return new ChampionDirectoryScopeGroups(builder);
    }
}
