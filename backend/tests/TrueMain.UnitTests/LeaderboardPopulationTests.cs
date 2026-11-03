using AwesomeAssertions;
using TrueMain.Services.Truemains.Leaderboard;
using Row = TrueMain.Services.Truemains.Leaderboard.LeaderboardPopulation.Row;

namespace TrueMain.UnitTests;

public sealed class LeaderboardPopulationTests
{
    private const int Top = 1;
    private const int Jungle = 2;
    private const int Middle = 4;
    private const int Ahri = 103;
    private const int Lux = 99;
    private const int Garen = 86;

    private static readonly string[] EveryPlatform = ["EUW1", "KR"];

    private static readonly Guid First = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Third = new("00000000-0000-0000-0000-000000000003");

    private static readonly LeaderboardPopulation Subject = new(
    [
        MainOf(First, "EUW1", Ahri, Middle, totalMatches: 40, playRate: 0.6),
        MainOf(First, "EUW1", Lux, Middle | Top, totalMatches: 40, playRate: 0.3),
        MainOf(Second, "KR", Garen, Top, totalMatches: 30, playRate: 0.9, isOtp: true),
        MainOf(Third, "EUW1", Lux, Jungle, totalMatches: 10, playRate: 0.5),
    ]);

    [Fact]
    public void AnUnfilteredBoardKeepsTheRankOrderAndCountsEachAccountOnce()
        => Subject.Select(Filter()).Should().Equal(First, Second, Third);

    [Fact]
    public void ThePlatformFilterKeepsOnlyItsAccounts()
        => Subject.Select(Filter(platforms: ["KR"])).Should().Equal(Second);

    [Fact]
    public void TheChampionFilterAdmitsAnAccountThroughAnyOfItsMains()
        => Subject.Select(Filter(championId: Lux)).Should().Equal(First, Third);

    [Fact]
    public void ThePositionFilterAdmitsAnAccountThroughAnyOfItsMains()
        => Subject.Select(Filter(position: "TOP")).Should().Equal(First, Second);

    [Fact]
    public void APositionTheBoardDoesNotKnowAdmitsNobody()
        => Subject.Select(Filter(position: "ROAMING")).Should().BeEmpty();

    [Fact]
    public void EveryFilterHasToLandOnTheSameMain()
    {
        // First has a Lux main, and a main played in the jungle by nobody: Lux is mid and top.
        Subject.Select(Filter(championId: Lux, position: "JUNGLE")).Should().Equal(Third);
        Subject.Select(Filter(championId: Ahri, position: "TOP")).Should().BeEmpty();
    }

    [Fact]
    public void TheOtpFilterAdmitsOnlyOneTricks()
        => Subject.Select(Filter(otpOnly: true)).Should().Equal(Second);

    [Fact]
    public void TheGamesFloorAppliesToTheMainThatAdmitsTheAccount()
        => Subject.Select(Filter(minGames: 20)).Should().Equal(First, Second);

    [Fact]
    public void DedicationRanksTheAdmittedAccountsOnTheirSignatureChampion()
    {
        // Second's play rate is the highest on the board; First's signature is Ahri at 0.6, not Lux.
        Subject.RankByDedication(Filter(), commitmentFloor: 0.2).Should().Equal(Second, First, Third);
    }

    [Fact]
    public void ALaneFilterNarrowsTheBoardWithoutChangingWhoRanksAboveWhom()
        => Subject.RankByDedication(Filter(position: "TOP"), commitmentFloor: 0.2).Should().Equal(Second, First);

    [Fact]
    public void TheChampionFilterScoresThatChampion()
    {
        // On Lux, Third's 0.5 beats First's 0.3, the other way around from their signature mains.
        Subject.RankByDedication(Filter(championId: Lux), commitmentFloor: 0.2).Should().Equal(Third, First);
    }

    [Fact]
    public void ATieFallsBackOnTheAccountId()
    {
        var tied = new LeaderboardPopulation(
        [
            MainOf(Second, "EUW1", Ahri, Middle, totalMatches: 40, playRate: 0.6),
            MainOf(First, "EUW1", Lux, Middle, totalMatches: 40, playRate: 0.6),
        ]);

        tied.RankByDedication(Filter(), commitmentFloor: 0.2).Should().Equal(First, Second);
    }

    private static LeaderboardFilter Filter(
        string[]? platforms = null,
        int? championId = null,
        string? position = null,
        int minGames = 0,
        bool otpOnly = false)
        => new(platforms ?? EveryPlatform, championId, position, minGames, otpOnly);

    private static Row MainOf(
        Guid account, string platform, int champion, int positions, int totalMatches, double playRate, bool isOtp = false)
        => new(account, platform, champion, totalMatches, isOtp, positions, playRate, ChampionMatches: 10, MasteryPoints: null, MasteryRank: null);
}
