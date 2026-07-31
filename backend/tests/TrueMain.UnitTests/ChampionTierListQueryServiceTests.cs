using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionTierListQueryServiceTests
{
    private static readonly string[] ValidTiers = ["S", "A", "B", "C", "D"];

    [Fact]
    public async Task GetTierListAsync_returns_empty_model_when_no_summaries()
    {
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChampionSummaryReadModel>());
        var service = new ChampionTierListQueryService(summaries, DefaultOptions());

        var result = await service.GetTierListAsync("16.5", position: null, eloBracket: null, CancellationToken.None);

        result.PatchVersion.Should().Be("16.5");
        result.Tiers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTierListAsync_groups_every_row_and_orders_tiers_strongest_first()
    {
        // 50 rows on a single position, win rate climbing, pick/ban rate held
        // constant — win rate is the sole discriminator, so this is still a
        // full pyramid (see ChampionTierCalculatorTests for the exact formula).
        var rows = Enumerable.Range(0, 50)
            .Select(i => Summary(championId: 100 + i, position: "MIDDLE",
                games: 300, wins: (int)Math.Round(300 * (0.40 + (i * 0.004))), pickRate: 0.05, banRate: 0.10))
            .ToList();
        var service = ServiceReturning(rows);

        var result = await service.GetTierListAsync(patch: null, position: null, eloBracket: null, CancellationToken.None);

        result.PatchVersion.Should().Be("16.5", "the resolved patch is read off the summary rows");

        var allEntries = result.Tiers.SelectMany(group => group.Entries).ToList();
        allEntries.Should().HaveCount(50, "every (champion, position) row is tiered");

        var emitted = result.Tiers.Select(group => group.Tier).ToList();
        emitted.Should().OnlyContain(tier => ValidTiers.Contains(tier));
        emitted.Should().Equal(emitted.OrderBy(tier => Array.IndexOf(ValidTiers, tier)),
            "tier groups are emitted strongest-first");
        emitted.Should().Contain("S").And.Contain("D");

        // The strongest seeded row (last, highest winRate) heads the S tier.
        var topEntry = result.Tiers.First(group => group.Tier == "S").Entries.First();
        topEntry.ChampionId.Should().Be(149, "the highest winRate row leads the top tier");
    }

    [Fact]
    public async Task GetTierListAsync_tiers_each_position_independently()
    {
        // MIDDLE has a wide winRate spread; TOP is a single dominant row. The
        // per-position tiering must let TOP's only row reach S without being
        // dragged down by the MIDDLE field — proof the buckets are role-local.
        var rows = new List<ChampionSummaryReadModel>
        {
            Summary(championId: 1, position: "TOP", games: 400, wins: 220, pickRate: 0.20, banRate: 0.15),
        };
        rows.AddRange(Enumerable.Range(0, 20)
            .Select(i => Summary(championId: 100 + i, position: "MIDDLE",
                games: 300, wins: (int)Math.Round(300 * (0.40 + (i * 0.005))), pickRate: 0.05, banRate: 0.10)));
        var service = ServiceReturning(rows);

        var result = await service.GetTierListAsync(patch: null, position: null, eloBracket: null, CancellationToken.None);

        var topRow = result.Tiers
            .SelectMany(group => group.Entries.Select(entry => (group.Tier, entry)))
            .Single(pair => pair.entry.ChampionId == 1);
        topRow.Tier.Should().Be("S", "TOP's only row is S among TOP rows regardless of the MIDDLE field");

        // The MIDDLE field still spans the pyramid on its own.
        var midTiers = result.Tiers
            .SelectMany(group => group.Entries.Where(entry => entry.Position == "MIDDLE").Select(_ => group.Tier))
            .Distinct()
            .ToList();
        midTiers.Should().Contain("S").And.Contain("D");
    }

    [Fact]
    public async Task GetTierListAsync_filters_to_requested_position()
    {
        var rows = new List<ChampionSummaryReadModel>
        {
            Summary(championId: 1, position: "TOP", games: 300, wins: 156, pickRate: 0.10, banRate: 0.10),
            Summary(championId: 2, position: "MIDDLE", games: 300, wins: 153, pickRate: 0.10, banRate: 0.10),
        };
        var service = ServiceReturning(rows);

        var result = await service.GetTierListAsync(patch: null, position: "TOP", eloBracket: null, CancellationToken.None);

        result.Position.Should().Be("TOP");
        result.Tiers.SelectMany(group => group.Entries)
            .Should().OnlyContain(entry => entry.Position == "TOP")
            .And.ContainSingle(entry => entry.ChampionId == 1);
    }

    [Fact]
    public async Task GetTierListAsync_handles_a_ban_data_free_patch()
    {
        // Every row null BanRate (pre-#920 patch) must still produce a valid
        // pyramid — the ban weight is folded back into pick + win.
        var rows = Enumerable.Range(0, 20)
            .Select(i => Summary(championId: 100 + i, position: "MIDDLE",
                games: 300, wins: (int)Math.Round(300 * (0.40 + (i * 0.01))), pickRate: 0.02 + (i * 0.005), banRate: null))
            .ToList();
        var service = ServiceReturning(rows);

        var result = await service.GetTierListAsync(patch: null, position: null, eloBracket: null, CancellationToken.None);

        result.Tiers.SelectMany(group => group.Entries).Should().HaveCount(20);
        result.Tiers.SelectMany(group => group.Entries).Should().OnlyContain(entry => entry.BanRate == null);
    }

    private static ChampionTierListQueryService ServiceReturning(IReadOnlyList<ChampionSummaryReadModel> rows)
    {
        var summaries = Substitute.For<IChampionSummariesQueryService>();
        summaries.GetAllSummariesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(rows);
        return new ChampionTierListQueryService(summaries, DefaultOptions());
    }

    private static IOptions<ChampionTierOptions> DefaultOptions() =>
        Microsoft.Extensions.Options.Options.Create(new ChampionTierOptions());

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
