using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionPatchDiffApiIntegrationTests
{
    private const int ChampionId = 157; // Yone
    private static readonly Guid AccountId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly PostgresFixture _fixture;

    public ChampionPatchDiffApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionPatchDiffAsync_DefaultsToTwoLatestPatchesAndReportsDeltas()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTwoPatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // No from/to → defaults to the two newest patches with data (16.4 → 16.5),
        // and no position → the dominant MIDDLE lane.
        var response = await client.GetAsync($"/champions/{ChampionId}/patch-diff");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = await response.Content.ReadFromJsonAsync<ChampionPatchDiffReadModel>();
        diff.Should().NotBeNull();
        diff!.ChampionId.Should().Be(ChampionId);
        diff.Position.Should().Be("MIDDLE");
        diff.AvailablePatchCount.Should().Be(2, "the champion has data on exactly two patches — the frontend keeps the section visible");

        diff.From.Should().NotBeNull();
        diff.To.Should().NotBeNull();
        diff.From!.Patch.Should().Be("16.4", "from defaults to the patch before the latest");
        diff.To!.Patch.Should().Be("16.5", "to defaults to the latest patch with data");

        // 16.4: 100 games, 40 wins → 0.40. 16.5: 100 games, 60 wins → 0.60.
        diff.From.WinRate.Should().BeApproximately(0.40, 1e-9);
        diff.To.WinRate.Should().BeApproximately(0.60, 1e-9);

        // Full core item path, not just the first item — this is the whole
        // point of surfacing ItemPath instead of a scalar TopFirstItemId.
        diff.From.ItemPath!.ItemIds.Should().Equal([3153, 3006, 3031]);
        diff.To.ItemPath!.ItemIds.Should().Equal([6672, 3006, 3031], "the popular first item shifted between patches");

        // Full rune page: primary/secondary tree stay put, only the first
        // item and skill order move on this fixture.
        diff.From.RunePage!.PrimaryStyleId.Should().Be(8000);
        diff.From.RunePage.PrimaryKeystoneId.Should().Be(8008);
        diff.From.RunePage.SecondaryStyleId.Should().Be(8400);
        diff.To.RunePage!.PrimaryStyleId.Should().Be(8000);
        diff.To.RunePage.PrimaryKeystoneId.Should().Be(8008);
        diff.To.RunePage.SecondaryStyleId.Should().Be(8400);

        // Full skill-order sequence on both sides.
        diff.From.SkillOrder!.Sequence.Should().Equal(["Q", "W", "E"]);
        diff.To.SkillOrder!.Sequence.Should().Equal(["Q", "E", "W"], "the dominant skill order shifted between patches");

        diff.Delta.Should().NotBeNull();
        diff.Delta!.WinRateChange.Should().BeApproximately(0.20, 1e-9, "0.60 - 0.40");
        diff.Delta.FirstItemChanged.Should().BeTrue("3153 → 6672");
        diff.Delta.SkillOrderChanged.Should().BeTrue("Q-W-E → Q-E-W");
        diff.Delta.KeystoneChanged.Should().BeFalse("the keystone stayed 8008 on both patches");
    }

    [Fact]
    public async Task GetChampionPatchDiffAsync_HonoursExplicitPatchesAndOrdersOldToNew()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTwoPatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Pass the patches reversed (newer as "from") — the service normalises
        // the pair so "from" is always the older one.
        var response = await client.GetAsync(
            $"/champions/{ChampionId}/patch-diff?from=16.5&to=16.4&position=middle");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = await response.Content.ReadFromJsonAsync<ChampionPatchDiffReadModel>();
        diff.Should().NotBeNull();
        diff!.From!.Patch.Should().Be("16.4", "the pair is normalised so from is the older patch");
        diff.To!.Patch.Should().Be("16.5");
        diff.Delta!.WinRateChange.Should().BeApproximately(0.20, 1e-9);
    }

    [Fact]
    public async Task GetChampionPatchDiffAsync_LoneToDefaultsFromToTheNeighbourBelowIt()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedThreePatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Only `to=16.4` given (not the newest 16.5). The defaulted `from` must
        // be the patch immediately older than the explicit `to` (16.3), NOT the
        // global newest — otherwise the user's explicit `to` would be discarded.
        var response = await client.GetAsync(
            $"/champions/{ChampionId}/patch-diff?to=16.4&position=middle");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = await response.Content.ReadFromJsonAsync<ChampionPatchDiffReadModel>();
        diff.Should().NotBeNull();
        diff!.To!.Patch.Should().Be("16.4", "the explicit to endpoint is honoured");
        diff.From!.Patch.Should().Be("16.3", "from defaults to the patch immediately older than to");
        diff.AvailablePatchCount.Should().Be(3, "the champion has data on three patches regardless of the two selected");
    }

    [Fact]
    public async Task GetChampionPatchDiffAsync_ReturnsNullSideForPatchWithoutData()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTwoPatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // 16.1 has no data for this champion → null "from" side, null delta,
        // but still a 200 so the page renders its own empty state.
        var response = await client.GetAsync(
            $"/champions/{ChampionId}/patch-diff?from=16.1&to=16.5&position=middle");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = await response.Content.ReadFromJsonAsync<ChampionPatchDiffReadModel>();
        diff.Should().NotBeNull();
        diff!.From.Should().BeNull("no aggregate exists on 16.1");
        diff.To.Should().NotBeNull();
        diff.Delta.Should().BeNull("a delta needs both sides");
    }

    [Fact]
    public async Task GetChampionPatchDiffAsync_ReturnsEmptyModelForUnknownChampion()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTwoPatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/99999/patch-diff");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = await response.Content.ReadFromJsonAsync<ChampionPatchDiffReadModel>();
        diff.Should().NotBeNull();
        diff!.From.Should().BeNull();
        diff.To.Should().BeNull();
        diff.Delta.Should().BeNull();
        diff.AvailablePatchCount.Should().Be(0, "no data at all — the frontend hides the whole section");
    }

    // Two MIDDLE patches for the champion: 16.4 (3153 first item, Q-W-E, 40% WR)
    // and 16.5 (6672 first item, Q-E-W, 60% WR). The first item, skill order and
    // win rate all move; the keystone (8008) stays put.
    private async Task SeedTwoPatchesAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = AccountId,
            PlatformId = "KR",
            Puuid = "patch-diff-puuid",
            GameName = "patch-diff-one",
            SummonerId = "patch-diff-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();

        seeder.AddPatternWithRune(
            AccountId, ChampionId, "16.4", "KR", 420, "MIDDLE",
            summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
            buildItems: [3153, 3006, 3031], bootsItemId: 3006,
            primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
            games: 100, wins: 40, aggregatedAtUtc: now);

        seeder.AddPatternWithRune(
            AccountId, ChampionId, "16.5", "KR", 420, "MIDDLE",
            summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-E-W",
            buildItems: [6672, 3006, 3031], bootsItemId: 3006,
            primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
            games: 100, wins: 60, aggregatedAtUtc: now);

        await seeder.SaveAsync(db);
    }

    // Three consecutive MIDDLE patches (16.3, 16.4, 16.5) so a lone explicit
    // endpoint has a neighbour to default against.
    private async Task SeedThreePatchesAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = AccountId,
            PlatformId = "KR",
            Puuid = "patch-diff-puuid",
            GameName = "patch-diff-one",
            SummonerId = "patch-diff-summoner",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var seeder = new ChampionAggregateSeeder();
        foreach (var (patch, wins) in new[] { ("16.3", 50), ("16.4", 40), ("16.5", 60) })
        {
            seeder.AddPatternWithRune(
                AccountId, ChampionId, patch, "KR", 420, "MIDDLE",
                summoner1Id: 4, summoner2Id: 12, skillOrderKey: "Q-W-E",
                buildItems: [3153, 3006, 3031], bootsItemId: 3006,
                primaryStyleId: 8000, primaryKeystoneId: 8008, secondaryStyleId: 8400,
                games: 100, wins: wins, aggregatedAtUtc: now);
        }

        await seeder.SaveAsync(db);
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
