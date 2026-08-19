using AwesomeAssertions;
using NSubstitute;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionOverviewQueryServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_reports_the_lifetime_total_not_the_served_patch_total()
    {
        // The homepage chip carries no patch qualifier, so it must not be a patch's
        // figure: it is every game the aggregate table holds. The served patch's own
        // total sits right there on the summaries result and is the easy wrong answer.
        var summaries = SummariesReturning(
            new ChampionSummariesResult
            {
                PatchVersion = "16.15",
                TotalGames = 24_310,
                Summaries = [Row(championId: 1, position: "TOP", tier: "S", games: 500)],
            },
            totalGames: 1_204_886);

        var overview = await new ChampionOverviewQueryService(summaries)
            .GetOverviewAsync(limit: 8, CancellationToken.None);

        overview.PatchVersion.Should().Be("16.15", "the teaser is still ranked on one patch");
        overview.GamesAnalyzed.Should().Be(1_204_886, "the chip spans every patch, not the served one");
    }

    [Fact]
    public async Task GetOverviewAsync_orders_rows_by_tier_then_games_and_truncates_to_the_limit()
    {
        var rows = new List<ChampionSummaryReadModel>
        {
            Row(championId: 1, position: "TOP", tier: "A", games: 900), // best games, but only A
            Row(championId: 2, position: "MIDDLE", tier: "S", games: 100), // S but fewer games than champion 3
            Row(championId: 3, position: "JUNGLE", tier: "S", games: 500), // S, most games among S rows
            Row(championId: 4, position: "BOTTOM", tier: "D", games: 50),
        };
        var service = ServiceReturning(rows);

        var overview = await service.GetOverviewAsync(limit: 2, CancellationToken.None);

        overview.TopRows.Should().HaveCount(2, "the limit truncates the field");
        overview.TopRows.Select(row => row.ChampionId).Should().Equal(new[] { 3, 2 },
            "S-tier rows lead regardless of games, and within S the busier row (champion 3) comes first");
    }

    [Fact]
    public async Task GetOverviewAsync_clamps_an_unrecognised_tier_to_the_bottom()
    {
        var rows = new List<ChampionSummaryReadModel>
        {
            Row(championId: 1, position: "TOP", tier: string.Empty, games: 900), // unassigned tier
            Row(championId: 2, position: "MIDDLE", tier: "D", games: 10),
        };
        var service = ServiceReturning(rows);

        var overview = await service.GetOverviewAsync(limit: 2, CancellationToken.None);

        overview.TopRows.Select(row => row.ChampionId).Should().Equal(new[] { 2, 1 },
            "even D outranks a row whose tier wasn't recognised, rather than the sort throwing");
    }

    private static ChampionOverviewQueryService ServiceReturning(IReadOnlyList<ChampionSummaryReadModel> rows)
    {
        var summaries = SummariesReturning(
            new ChampionSummariesResult
            {
                PatchVersion = "16.5",
                TotalGames = rows.Sum(row => (long)row.Games),
                Summaries = rows,
            },
            totalGames: rows.Sum(row => (long)row.Games));
        return new ChampionOverviewQueryService(summaries);
    }

    private static IChampionSummariesQueryService SummariesReturning(
        ChampionSummariesResult result, long totalGames)
    {
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(patch: null, eloBracket: null, Arg.Any<CancellationToken>())
            .Returns(result);
        summaries.GetTotalGamesAsync(Arg.Any<CancellationToken>()).Returns(totalGames);
        return summaries;
    }

    private static ChampionSummaryReadModel Row(int championId, string position, string tier, int games) => new()
    {
        ChampionId = championId,
        Position = position,
        Tier = tier,
        Games = games,
        Wins = games / 2,
        WinRate = 0.5,
        PickRate = 0.05,
        PatchVersion = "16.5",
    };
}
