using AwesomeAssertions;
using NSubstitute;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

/// <summary>
/// The tier list no longer tiers (#1240): it regroups the rows
/// <c>ChampionSummariesQueryService</c> already stamped. These tests therefore
/// feed rows stamped the same way that service stamps them — one
/// <c>ChampionTierCalculator.Evaluate</c> call per position — and assert the
/// reshaping. The tiering formula itself lives in ChampionTierCalculatorTests.
/// </summary>
public sealed class ChampionTierListQueryServiceTests
{
    private static readonly string[] ValidTiers = ["S", "A", "B", "C", "D"];

    [Fact]
    public async Task GetTierListAsync_returns_empty_model_when_no_summaries()
    {
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ChampionSummariesResult { PatchVersion = "16.5" });
        var service = new ChampionTierListQueryService(summaries, TestChampionReadCache.PassThrough());

        ChampionTierListReadModel result =
            await service.GetTierListAsync("16.5", position: null, eloBracket: null, truemainsOnly: true, CancellationToken.None);

        result.PatchVersion.Should().Be("16.5");
        result.Tiers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTierListAsync_groups_every_row_and_orders_tiers_strongest_first()
    {
        // 50 rows on a single position, win rate climbing, pick/ban rate held
        // constant — win rate is the sole discriminator, so this is still a
        // full pyramid (see ChampionTierCalculatorTests for the exact formula).
        List<ChampionSummaryReadModel> rows = Stamp(Enumerable.Range(0, 50)
            .Select(i => Summary(championId: 100 + i, position: "MIDDLE",
                games: 300, wins: (int)Math.Round(300 * (0.40 + (i * 0.004))), pickRate: 0.05, banRate: 0.10)));
        ChampionTierListQueryService service = ServiceReturning(rows);

        ChampionTierListReadModel result =
            await service.GetTierListAsync(patch: null, position: null, eloBracket: null, truemainsOnly: true, CancellationToken.None);

        result.PatchVersion.Should().Be("16.5", "the resolved patch is read off the summary rows");

        List<ChampionTierEntryReadModel> allEntries = result.Tiers.SelectMany(group => group.Entries).ToList();
        allEntries.Should().HaveCount(50, "every (champion, position) row is emitted exactly once");

        List<string> emitted = result.Tiers.Select(group => group.Tier).ToList();
        emitted.Should().OnlyContain(tier => ValidTiers.Contains(tier));
        emitted.Should().Equal(emitted.OrderBy(tier => Array.IndexOf(ValidTiers, tier)),
            "tier groups are emitted strongest-first");
        emitted.Should().Contain("S").And.Contain("D");

        // The strongest seeded row (last, highest winRate) heads the S tier.
        ChampionTierEntryReadModel topEntry = result.Tiers.First(group => group.Tier == "S").Entries.First();
        topEntry.ChampionId.Should().Be(149, "the highest winRate row leads the top tier");
    }

    [Fact]
    public async Task GetTierListAsync_carries_the_tier_each_row_arrives_with()
    {
        // The whole point of #1240: the letter on the group is the row's own
        // Tier, not a second opinion computed here. Rows stamped by hand with
        // a letter the formula would never produce must come back untouched.
        List<ChampionSummaryReadModel> rows =
        [
            Summary(1, "TOP", 300, 150, 0.10, 0.10) with { Tier = "D", TierScore = 0.9 },
            Summary(2, "TOP", 300, 150, 0.10, 0.10) with { Tier = "D", TierScore = 0.1 },
            Summary(3, "MIDDLE", 300, 150, 0.10, 0.10) with { Tier = "S", TierScore = 0.5 },
        ];
        ChampionTierListQueryService service = ServiceReturning(rows);

        ChampionTierListReadModel result =
            await service.GetTierListAsync(patch: null, position: null, eloBracket: null, truemainsOnly: true, CancellationToken.None);

        result.Tiers.Select(group => group.Tier).Should().Equal(new[] { "S", "D" });
        result.Tiers.Single(group => group.Tier == "S").Entries
            .Select(entry => entry.ChampionId).Should().Equal(3);
        result.Tiers.Single(group => group.Tier == "D").Entries
            .Select(entry => entry.ChampionId).Should().Equal(new[] { 1, 2 },
                "entries are ordered by the score they were stamped with, strongest first");
    }

    [Fact]
    public async Task GetTierListAsync_breaks_score_ties_on_champion_id()
    {
        List<ChampionSummaryReadModel> rows =
        [
            Summary(7, "TOP", 300, 150, 0.10, 0.10) with { Tier = "B", TierScore = 0.5 },
            Summary(3, "TOP", 300, 150, 0.10, 0.10) with { Tier = "B", TierScore = 0.5 },
        ];
        ChampionTierListQueryService service = ServiceReturning(rows);

        ChampionTierListReadModel result =
            await service.GetTierListAsync(patch: null, position: null, eloBracket: null, truemainsOnly: true, CancellationToken.None);

        result.Tiers.Single().Entries.Select(entry => entry.ChampionId).Should().Equal(3, 7);
    }

    [Fact]
    public async Task GetTierListAsync_keeps_a_lane_tier_identical_when_scoped_to_it()
    {
        // The position filter drops whole lanes, never rows inside a kept lane,
        // and the stamped tier is already lane-relative — which is exactly why
        // re-tiering the filtered set was a no-op worth deleting.
        List<ChampionSummaryReadModel> rows = Stamp(
        [
            Summary(championId: 1, position: "TOP", games: 400, wins: 220, pickRate: 0.20, banRate: 0.15),
            .. Enumerable.Range(0, 20).Select(i => Summary(championId: 100 + i, position: "MIDDLE",
                games: 300, wins: (int)Math.Round(300 * (0.40 + (i * 0.005))), pickRate: 0.05, banRate: 0.10)),
        ]);
        ChampionTierListQueryService service = ServiceReturning(rows);

        ChampionTierListReadModel unscoped =
            await service.GetTierListAsync(patch: null, position: null, eloBracket: null, truemainsOnly: true, CancellationToken.None);
        ChampionTierListReadModel scoped =
            await service.GetTierListAsync(patch: null, position: "MIDDLE", eloBracket: null, truemainsOnly: true, CancellationToken.None);

        (int ChampionId, string Tier)[] unscopedMiddle = [.. unscoped.Tiers
            .SelectMany(group => group.Entries
                .Where(entry => entry.Position == "MIDDLE")
                .Select(entry => (entry.ChampionId, group.Tier)))
            .OrderBy(pair => pair.ChampionId)];
        (int ChampionId, string Tier)[] scopedMiddle = [.. scoped.Tiers
            .SelectMany(group => group.Entries.Select(entry => (entry.ChampionId, group.Tier)))
            .OrderBy(pair => pair.ChampionId)];

        scopedMiddle.Should().Equal(unscopedMiddle);
        unscoped.Tiers
            .SelectMany(group => group.Entries.Select(entry => (group.Tier, entry.ChampionId)))
            .Single(pair => pair.ChampionId == 1).Tier
            .Should().Be("S", "TOP's only row is S among TOP rows regardless of the MIDDLE field");
    }

    [Fact]
    public async Task GetTierListAsync_filters_to_requested_position()
    {
        List<ChampionSummaryReadModel> rows = Stamp(
        [
            Summary(championId: 1, position: "TOP", games: 300, wins: 156, pickRate: 0.10, banRate: 0.10),
            Summary(championId: 2, position: "MIDDLE", games: 300, wins: 153, pickRate: 0.10, banRate: 0.10),
        ]);
        ChampionTierListQueryService service = ServiceReturning(rows);

        ChampionTierListReadModel result =
            await service.GetTierListAsync(patch: null, position: "TOP", eloBracket: null, truemainsOnly: true, CancellationToken.None);

        result.Position.Should().Be("TOP");
        result.Tiers.SelectMany(group => group.Entries)
            .Should().OnlyContain(entry => entry.Position == "TOP")
            .And.ContainSingle(entry => entry.ChampionId == 1);
    }

    [Fact]
    public async Task GetTierListAsync_handles_a_ban_data_free_patch()
    {
        // Every row null BanRate (pre-#920 patch) must still round-trip its
        // null rather than showing a fabricated 0%.
        List<ChampionSummaryReadModel> rows = Stamp(Enumerable.Range(0, 20)
            .Select(i => Summary(championId: 100 + i, position: "MIDDLE",
                games: 300, wins: (int)Math.Round(300 * (0.40 + (i * 0.01))),
                pickRate: 0.02 + (i * 0.005), banRate: null)));
        ChampionTierListQueryService service = ServiceReturning(rows);

        ChampionTierListReadModel result =
            await service.GetTierListAsync(patch: null, position: null, eloBracket: null, truemainsOnly: true, CancellationToken.None);

        result.Tiers.SelectMany(group => group.Entries).Should().HaveCount(20);
        result.Tiers.SelectMany(group => group.Entries).Should().OnlyContain(entry => entry.BanRate == null);
    }

    /// <summary>
    /// Stamps Tier/TierScore the way <c>ChampionSummariesQueryService.AssignTiers</c>
    /// does — one calculator call per position — so these tests exercise the
    /// rows the real directory hands the tier list.
    /// </summary>
    private static List<ChampionSummaryReadModel> Stamp(IEnumerable<ChampionSummaryReadModel> summaries)
    {
        List<ChampionSummaryReadModel> rows = [.. summaries];
        ChampionTierOptions options = new();

        foreach (IGrouping<string, int> lane in Enumerable.Range(0, rows.Count)
                     .GroupBy(index => rows[index].Position, StringComparer.Ordinal))
        {
            List<int> indices = [.. lane];
            List<ChampionTierCalculator.TierInput> inputs =
            [
                .. indices.Select(index => new ChampionTierCalculator.TierInput(
                    rows[index].Position, rows[index].Games, rows[index].Wins,
                    rows[index].PickRate, rows[index].BanRate)),
            ];
            IReadOnlyList<ChampionTierCalculator.TierResult> results =
                ChampionTierCalculator.Evaluate(inputs, options);

            for (int i = 0; i < indices.Count; i++)
            {
                rows[indices[i]] = rows[indices[i]] with { Tier = results[i].Tier, TierScore = results[i].Score };
            }
        }

        return rows;
    }

    private static ChampionTierListQueryService ServiceReturning(IReadOnlyList<ChampionSummaryReadModel> rows)
    {
        IChampionSummariesQueryService summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ChampionSummariesResult
            {
                PatchVersion = "16.5",
                TotalGames = rows.Sum(row => row.Games),
                Summaries = rows,
            });
        return new ChampionTierListQueryService(summaries, TestChampionReadCache.PassThrough());
    }

    private static ChampionSummaryReadModel Summary(
        int championId, string position, int games, int wins, double pickRate, double? banRate) => new()
        {
            ChampionId = championId,
            Position = position,
            Games = games,
            Wins = wins,
            WinRate = (double)wins / games,
            PickRate = pickRate,
            BanRate = banRate,
            PatchVersion = "16.5",
        };
}
