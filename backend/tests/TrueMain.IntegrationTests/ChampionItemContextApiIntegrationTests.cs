using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.ItemContext;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The read surface of the situational build context (#1451): one range read of the
/// verdicts the fold wrote (#1450), projected — no statistics on this side.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionItemContextApiIntegrationTests
{
    private const int Champion = 266;
    private const string Position = "TOP";
    private const string Patch = "16.4";
    private const string OlderPatch = "16.3";

    private readonly PostgresFixture _fixture;

    public ChampionItemContextApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetItemContext_ProjectsTheVerdictWithItsFindings()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        var response = await GetAsync($"/champions/{Champion}/item-context?position={Position}&patch={Patch}");

        response.Patch.Should().Be(Patch);
        response.AllRanks.Should().BeTrue("the verdicts carry no rank dimension, and the client must not assume one");
        response.Items.Should().HaveCount(2);

        var situational = response.Items.Single(item => item.ItemId == 3065);
        situational.Slot.Should().Be("Build");
        situational.Class.Should().Be("Situational");
        situational.Games.Should().Be(300);
        situational.SlotGames.Should().Be(1000);
        situational.PickRate.Should().BeApproximately(0.3, 1e-9);
        situational.WinRate.Should().BeApproximately(0.5, 1e-9);

        var finding = situational.Axes.Should().ContainSingle().Subject;
        finding.Axis.Should().Be("EnemyMagicDamage");
        finding.Bucket.Should().Be("High");
        finding.DraftTime.Should().BeTrue();
        finding.RateIn.Should().BeApproximately(0.62, 1e-9);
        finding.RateOut.Should().BeApproximately(0.18, 1e-9);
        finding.PatchWindow.Should().Be(2);

        var core = response.Items.Single(item => item.ItemId == 6632);
        core.Class.Should().Be("Core");
        core.Axes.Should().BeEmpty("a core item has no situation to explain");
    }

    [Fact]
    public async Task GetItemContext_MarksTheInGameAxisAsSuch()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(inGameAxis: true);

        var response = await GetAsync($"/champions/{Champion}/item-context?position={Position}&patch={Patch}");

        var finding = response.Items.Single(item => item.ItemId == 3065).Axes.Single();
        finding.Axis.Should().Be("OwnGoldLeadAt15");
        finding.DraftTime.Should().BeFalse("a reader cannot act on it at champion select, and the card has to say so");
    }

    [Fact]
    public async Task GetItemContext_ServesTheNewestPatch_WhenTheCallerSendsNone()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();
        await SeedVerdictAsync(OlderPatch, 3065, ItemContextClass.Preference, games: 10, slotGames: 100, axes: []);

        var response = await GetAsync($"/champions/{Champion}/item-context?position={Position}");

        response.Patch.Should().Be(Patch);
    }

    [Fact]
    public async Task GetItemContext_ReturnsAnEmptyListForASliceWithNoVerdicts()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await GetAsync($"/champions/{Champion}/item-context?position={Position}");

        response.Items.Should().BeEmpty("nothing measured is a state, not an error");
        response.Patch.Should().BeNull();
    }

    [Fact]
    public async Task GetItemContext_RequiresAPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/item-context");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<ChampionItemContextResponse> GetAsync(string url)
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);
        return (await client.GetFromJsonAsync<ChampionItemContextResponse>(url))!;
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);

    private async Task SeedAsync(bool inGameAxis = false)
    {
        await SeedVerdictAsync(Patch, 3065, ItemContextClass.Situational, games: 300, slotGames: 1000, axes:
        [
            new ItemContextAxisFinding
            {
                Axis = inGameAxis ? ItemContextAxis.OwnGoldLeadAt15 : ItemContextAxis.EnemyMagicDamage,
                Bucket = ItemContextBucket.High,
                GamesIn = 310,
                TotalIn = 500,
                GamesOut = 90,
                TotalOut = 500,
                Lift = 0.44,
                Z = 14.2,
                PatchWindow = 2,
            },
        ]);

        await SeedVerdictAsync(Patch, 6632, ItemContextClass.Core, games: 940, slotGames: 1000, axes: []);
    }

    private async Task SeedVerdictAsync(
        string patch,
        int itemId,
        ItemContextClass verdictClass,
        int games,
        int slotGames,
        List<ItemContextAxisFinding> axes)
    {
        await using var db = _fixture.CreateDbContext();
        db.ChampionItemContextVerdicts.Add(new ChampionItemContextVerdict
        {
            ChampionId = Champion,
            Position = Position,
            Patch = patch,
            Slot = ItemContextSlot.Build,
            ItemId = itemId,
            Games = games,
            Wins = games / 2,
            SlotGames = slotGames,
            PickRate = games / (double)slotGames,
            Class = verdictClass,
            PatchWindow = axes.Count > 0 ? axes.Max(axis => axis.PatchWindow) : 1,
            Axes = axes,
            AggregatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
