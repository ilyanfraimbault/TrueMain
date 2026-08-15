using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionOverviewQueryServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_sums_the_volume_chips_across_the_patch_window()
    {
        // The homepage figures span the served patch and the one before it (#1109),
        // so the site's headline volume does not collapse to a handful of games every
        // time a patch rolls over. The teaser stays on the served patch alone.
        var summaries = SummariesReturning(
            new ChampionSummariesResult
            {
                PatchVersion = "16.15",
                TotalGames = 24_310,
                ChampionsRanked = 168,
                Summaries = [Row(championId: 1, position: "TOP", tier: "S", games: 500)],
            },
            Volume("16.15", totalGames: 24_310, championsPastFloor: [1, 2, 3]),
            Volume("16.14", totalGames: 40_000, championsPastFloor: [3, 4]));

        var overview = await Service(summaries).GetOverviewAsync(limit: 8, CancellationToken.None);

        overview.PatchVersion.Should().Be("16.15", "the teaser is still ranked on one patch");
        overview.GamesAnalyzed.Should().Be(64_310, "both patches in the window contribute their games");
        overview.ChampionsRanked.Should().Be(4,
            "champion 3 is ranked on both patches and must be counted once, not twice");
        overview.CountedPatches.Should().Equal("16.15", "16.14");
    }

    [Fact]
    public async Task GetOverviewAsync_falls_back_to_the_served_patch_totals_when_no_volume_is_measured()
    {
        // TotalGames on the underlying result already counts every aggregated group —
        // including rows the ranked directory drops (#972) — so with no window to sum
        // the overview must pass it through untouched rather than reporting zero
        // beside a populated teaser.
        var summaries = SummariesReturning(
            new ChampionSummariesResult
            {
                PatchVersion = "16.5",
                TotalGames = 24_310,
                ChampionsRanked = 168,
                Summaries = [Row(championId: 1, position: "TOP", tier: "S", games: 500)],
            });

        var overview = await Service(summaries).GetOverviewAsync(limit: 8, CancellationToken.None);

        overview.PatchVersion.Should().Be("16.5");
        overview.GamesAnalyzed.Should().Be(24_310,
            "TotalGames already accounts for below-floor and position-less rows the ranked directory drops");
        overview.ChampionsRanked.Should().Be(168);
        // `Equal(params string[])` would read a `because` argument as a second expected
        // element, so the single-item case is asserted this way round.
        overview.CountedPatches.Should().ContainSingle("the chips still say which patch they cover")
            .Which.Should().Be("16.5");
    }

    [Fact]
    public async Task GetOverviewAsync_asks_for_the_configured_patch_window()
    {
        var summaries = SummariesReturning(new ChampionSummariesResult { PatchVersion = "16.15" });
        var options = new ChampionsListOptions { HomepagePatchWindow = 3 };

        await Service(summaries, options).GetOverviewAsync(limit: 8, CancellationToken.None);

        await summaries.Received(1).GetServedPatchVolumesAsync(3, Arg.Any<CancellationToken>());
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
        var summaries = SummariesReturning(new ChampionSummariesResult
        {
            PatchVersion = "16.5",
            TotalGames = rows.Sum(row => (long)row.Games),
            ChampionsRanked = rows.Select(row => row.ChampionId).Distinct().Count(),
            Summaries = rows,
        });
        return Service(summaries);
    }

    private static IChampionSummariesQueryService SummariesReturning(
        ChampionSummariesResult result, params ChampionPatchVolume[] volumes)
    {
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(patch: null, eloBracket: null, Arg.Any<CancellationToken>())
            .Returns(result);
        summaries.GetServedPatchVolumesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ChampionPatchVolume>>(volumes);
        return summaries;
    }

    private static ChampionOverviewQueryService Service(
        IChampionSummariesQueryService summaries, ChampionsListOptions? options = null)
        // Fully qualified: `TrueMain.Options` is a namespace in scope here, so a bare
        // `Options.Create` binds to it instead of the extensions helper.
        => new(summaries, Microsoft.Extensions.Options.Options.Create(options ?? new ChampionsListOptions()));

    private static ChampionPatchVolume Volume(string patch, long totalGames, IReadOnlyList<int> championsPastFloor)
        => new()
        {
            Patch = patch,
            TotalGames = totalGames,
            LinesPastFloor = championsPastFloor.Count,
            ChampionsPastFloor = championsPastFloor,
        };

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
