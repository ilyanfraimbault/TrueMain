using AwesomeAssertions;
using NSubstitute;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionOverviewQueryServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_copies_the_true_total_and_champions_ranked_from_the_summaries_result()
    {
        // TotalGames on the underlying result already counts every aggregated
        // group — including rows the ranked directory drops (#972) — so the
        // overview must pass it through untouched, not re-derive it from
        // Summaries (which would silently undo the fix this endpoint exists for).
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(patch: null, eloBracket: null, Arg.Any<CancellationToken>())
            .Returns(new ChampionSummariesResult
            {
                PatchVersion = "16.5",
                TotalGames = 24_310,
                ChampionsRanked = 168,
                Summaries = [Row(championId: 1, position: "TOP", tier: "S", games: 500)],
            });
        var service = new ChampionOverviewQueryService(summaries);

        var overview = await service.GetOverviewAsync(limit: 8, CancellationToken.None);

        overview.PatchVersion.Should().Be("16.5");
        overview.GamesAnalyzed.Should().Be(24_310, "TotalGames already accounts for below-floor and position-less rows the ranked directory drops");
        overview.ChampionsRanked.Should().Be(168);
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
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(patch: null, eloBracket: null, Arg.Any<CancellationToken>())
            .Returns(new ChampionSummariesResult
            {
                PatchVersion = "16.5",
                TotalGames = rows.Sum(row => (long)row.Games),
                ChampionsRanked = rows.Select(row => row.ChampionId).Distinct().Count(),
                Summaries = rows,
            });
        return new ChampionOverviewQueryService(summaries);
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
