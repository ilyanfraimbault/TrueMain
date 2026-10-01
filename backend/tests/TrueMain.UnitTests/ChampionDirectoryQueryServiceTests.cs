using AwesomeAssertions;
using NSubstitute;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Directory;

namespace TrueMain.UnitTests;

public sealed class ChampionDirectoryQueryServiceTests
{
    [Theory]
    [InlineData("pickRate", ChampionDirectorySort.PickRate)]
    [InlineData("WINRATE", ChampionDirectorySort.WinRate)]
    [InlineData(" banRate ", ChampionDirectorySort.BanRate)]
    [InlineData("games", ChampionDirectorySort.Games)]
    [InlineData("tier", ChampionDirectorySort.Tier)]
    public void TryParseSort_reads_every_column_case_insensitively(string raw, ChampionDirectorySort expected)
    {
        ChampionDirectoryOrdering.TryParseSort(raw, out var sort).Should().BeTrue();
        sort.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("name")]
    public void TryParseSort_falls_back_to_pick_rate(string? raw)
    {
        ChampionDirectoryOrdering.TryParseSort(raw, out var sort).Should().BeFalse();
        sort.Should().Be(ChampionDirectorySort.PickRate);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("desc", true)]
    [InlineData("sideways", true)]
    [InlineData("ASC", false)]
    public void IsDescending_reads_only_asc_as_ascending(string? raw, bool expected)
        => ChampionDirectoryOrdering.IsDescending(raw).Should().Be(expected);

    [Theory]
    [InlineData(ChampionDirectorySort.PickRate, true, new[] { 2, 3, 1 })]
    [InlineData(ChampionDirectorySort.PickRate, false, new[] { 1, 3, 2 })]
    [InlineData(ChampionDirectorySort.WinRate, true, new[] { 1, 2, 3 })]
    [InlineData(ChampionDirectorySort.WinRate, false, new[] { 3, 2, 1 })]
    [InlineData(ChampionDirectorySort.Games, true, new[] { 3, 1, 2 })]
    [InlineData(ChampionDirectorySort.Games, false, new[] { 2, 1, 3 })]
    public void Apply_orders_by_the_column_in_both_directions(
        ChampionDirectorySort sort, bool descending, int[] expected)
    {
        var rows = new[]
        {
            Row(championId: 1, pickRate: 0.01, winRate: 0.55, games: 500),
            Row(championId: 2, pickRate: 0.09, winRate: 0.52, games: 100),
            Row(championId: 3, pickRate: 0.05, winRate: 0.48, games: 900),
        };

        ChampionDirectoryOrdering.Apply(rows, sort, descending)
            .Select(row => row.ChampionId).Should().Equal(expected);
    }

    [Theory]
    [InlineData(true, new[] { 2, 1, 3 })]
    [InlineData(false, new[] { 1, 2, 3 })]
    public void Apply_keeps_an_unobserved_ban_rate_last_in_both_directions(bool descending, int[] expected)
    {
        var rows = new[]
        {
            Row(championId: 1, banRate: 0.02),
            Row(championId: 2, banRate: 0.30),
            Row(championId: 3, banRate: null),
        };

        ChampionDirectoryOrdering.Apply(rows, ChampionDirectorySort.BanRate, descending)
            .Select(row => row.ChampionId).Should().Equal(expected,
                "a patch that predates ban ingestion has no rate, which is not the lowest one");
    }

    [Theory]
    [InlineData(true, new[] { 2, 3, 1, 4 })]
    [InlineData(false, new[] { 1, 3, 2, 4 })]
    public void Apply_orders_by_tier_then_tier_score_and_keeps_an_unknown_tier_last(bool descending, int[] expected)
    {
        var rows = new[]
        {
            Row(championId: 1, tier: "C", tierScore: 0.2),
            Row(championId: 2, tier: "S", tierScore: 0.9),
            Row(championId: 3, tier: "S", tierScore: 0.7),
            Row(championId: 4, tier: string.Empty, tierScore: 1.0),
        };

        ChampionDirectoryOrdering.Apply(rows, ChampionDirectorySort.Tier, descending)
            .Select(row => row.ChampionId).Should().Equal(expected);
    }

    [Fact]
    public void Apply_breaks_ties_on_pick_rate_games_champion_then_lane()
    {
        var rows = new[]
        {
            Row(championId: 7, position: "TOP", winRate: 0.5, pickRate: 0.02, games: 100),
            Row(championId: 5, position: "MIDDLE", winRate: 0.5, pickRate: 0.02, games: 100),
            Row(championId: 5, position: "BOTTOM", winRate: 0.5, pickRate: 0.02, games: 100),
            Row(championId: 9, position: "TOP", winRate: 0.5, pickRate: 0.02, games: 300),
            Row(championId: 8, position: "TOP", winRate: 0.5, pickRate: 0.04, games: 50),
        };

        ChampionDirectoryOrdering.Apply(rows, ChampionDirectorySort.WinRate, descending: true)
            .Select(row => $"{row.ChampionId}-{row.Position}")
            .Should().Equal("8-TOP", "9-TOP", "5-BOTTOM", "5-MIDDLE", "7-TOP");
    }

    [Fact]
    public async Task GetPageAsync_filters_by_lane_and_champion_before_paging()
    {
        var service = ServiceReturning(
            Row(championId: 1, position: "TOP", pickRate: 0.03),
            Row(championId: 1, position: "MIDDLE", pickRate: 0.02),
            Row(championId: 2, position: "TOP", pickRate: 0.05),
            Row(championId: 3, position: "JUNGLE", pickRate: 0.04));

        var topLane = await service.GetPageAsync(Request(position: "TOP"), CancellationToken.None);
        topLane.Rows.Select(row => row.ChampionId).Should().Equal(2, 1);
        topLane.Total.Should().Be(2);

        var oneChampion = await service.GetPageAsync(Request(championId: 1), CancellationToken.None);
        oneChampion.Rows.Select(row => row.Position).Should().Equal("TOP", "MIDDLE");
        oneChampion.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetPageAsync_slices_the_page_and_reports_the_filtered_total()
    {
        var service = ServiceReturning(Enumerable.Range(1, 7)
            .Select(id => Row(championId: id, pickRate: id / 100.0))
            .ToArray());

        var page = await service.GetPageAsync(Request(page: 2, pageSize: 3), CancellationToken.None);

        page.Rows.Select(row => row.ChampionId).Should().Equal(4, 3, 2);
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(3);
        page.Total.Should().Be(7);
        page.PatchVersion.Should().Be("16.5");
    }

    [Fact]
    public async Task GetPageAsync_answers_a_page_past_the_end_with_no_rows_and_the_real_total()
    {
        var service = ServiceReturning(Row(championId: 1), Row(championId: 2));

        var page = await service.GetPageAsync(Request(page: int.MaxValue, pageSize: 100), CancellationToken.None);

        page.Rows.Should().BeEmpty();
        page.Total.Should().Be(2, "the pager needs the real total to step back into range");
    }

    [Fact]
    public async Task GetPageAsync_carries_the_patch_even_when_no_line_matches()
    {
        var service = ServiceReturning(Row(championId: 1, position: "TOP"));

        var page = await service.GetPageAsync(Request(position: "UTILITY"), CancellationToken.None);

        page.Rows.Should().BeEmpty();
        page.Total.Should().Be(0);
        page.PatchVersion.Should().Be("16.5", "the patch picker reads it from the envelope, not from a row");
    }

    private static ChampionDirectoryRequest Request(
        string? position = null, int? championId = null, int page = 1, int pageSize = 50)
        => new(
            Patch: null,
            EloBracket: null,
            TruemainsOnly: true,
            Position: position,
            ChampionId: championId,
            Sort: ChampionDirectorySort.PickRate,
            Descending: true,
            Page: page,
            PageSize: pageSize);

    private static ChampionDirectoryQueryService ServiceReturning(params ChampionSummaryReadModel[] rows)
    {
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ChampionSummariesResult { PatchVersion = "16.5", Summaries = rows });
        return new ChampionDirectoryQueryService(summaries, TestChampionReadCache.PassThrough());
    }

    private static ChampionSummaryReadModel Row(
        int championId,
        string position = "TOP",
        double pickRate = 0.05,
        double winRate = 0.5,
        int games = 200,
        double? banRate = 0.1,
        string tier = "B",
        double tierScore = 0.5) => new()
    {
        ChampionId = championId,
        Position = position,
        PickRate = pickRate,
        WinRate = winRate,
        Games = games,
        Wins = (int)(games * winRate),
        BanRate = banRate,
        Tier = tier,
        TierScore = tierScore,
        PatchVersion = "16.5",
    };
}
