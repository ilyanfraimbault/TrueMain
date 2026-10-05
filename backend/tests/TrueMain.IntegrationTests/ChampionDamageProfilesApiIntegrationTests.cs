using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Data.Statics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The damage-profile endpoint (#1905): the profile snapshot the item-context fold reads,
/// served whole, with the per-archetype split and a static class for unprofiled champions.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionDamageProfilesApiIntegrationTests : IAsyncLifetime
{
    private const string Patch = "16.19";
    private const string OlderPatch = "16.18";

    private const int KaiSa = 145;
    private const int Ahri = 103;
    private const int Garen = 86;
    private const int Leona = 89;
    private const int Zed = 238;
    private const int Unrated = 999;

    private readonly PostgresFixture _fixture;

    public ChampionDamageProfilesApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task DamageProfiles_ServeEveryResolvedProfile_WithSharesSummingToOne_AndThePatchUsed()
    {
        await SeedAsync();

        var response = await GetAsync();

        response.Patch.Should().Be(Patch, "the newest profiled patch is served when none is asked for");

        var measured = response.Profiles.Where(p => p.Source == ChampionDamageProfileSources.Measured).ToList();
        measured.Should().OnlyContain(p =>
            Math.Abs(p.PhysicalShare!.Value + p.MagicShare!.Value + p.TrueShare!.Value - 1) < 1e-9
            && p.DamagePerGame > 0
            && p.DamageClass == null);

        var ahri = measured.Single(p => p.ChampionId == Ahri && p.Position == "MIDDLE");
        ahri.MagicShare.Should().BeApproximately(0.8, 1e-9);
        ahri.Games.Should().Be(500);

        var garen = measured.Single(p => p.ChampionId == Garen && p.Position == "TOP");
        garen.Patch.Should().Be(OlderPatch, "the snapshot reaches back for a champion the served patch does not cover");

        // The champion-wide entry a pick at any other lane resolves to.
        var kaisaAnywhere = measured.Single(p => p.ChampionId == KaiSa && p.Position == null);
        kaisaAnywhere.ProfilePosition.Should().Be("BOTTOM");
    }

    [Fact]
    public async Task DamageProfiles_SplitAFlexChampionByBuild_AndNotAOneBuildOne()
    {
        await SeedAsync();

        var response = await GetAsync();

        var kaisa = response.Profiles.Single(p => p.ChampionId == KaiSa && p.Position == "BOTTOM");
        kaisa.Builds.Select(build => build.Archetype).Should().Equal("OnHit", "AbilityPower");
        kaisa.Builds[0].PhysicalShare.Should().BeGreaterThan(kaisa.Builds[0].MagicShare);
        kaisa.Builds[1].MagicShare.Should().BeGreaterThan(kaisa.Builds[1].PhysicalShare);
        kaisa.FlexDamage.Should().BeTrue();

        var ahri = response.Profiles.Single(p => p.ChampionId == Ahri && p.Position == "MIDDLE");
        ahri.Builds.Should().ContainSingle().Which.Archetype.Should().Be("AbilityPower");
        ahri.FlexDamage.Should().BeFalse();
    }

    [Fact]
    public async Task DamageProfiles_ClassifyAnUnprofiledChampion_WithoutAnyShare()
    {
        await SeedAsync();

        var response = await GetAsync();

        var zed = response.Profiles.Should().ContainSingle(p => p.ChampionId == Zed,
            "fifty games are below the floor, so Zed is not measured — only classified").Which;
        zed.Source.Should().Be(ChampionDamageProfileSources.Fallback);
        zed.DamageClass.Should().Be("physical");
        zed.PhysicalShare.Should().BeNull();
        zed.MagicShare.Should().BeNull();
        zed.TrueShare.Should().BeNull();
        zed.Games.Should().BeNull();

        response.Profiles.Should().NotContain(p => p.ChampionId == Unrated, "no rating is no class, never a guess");
        response.Profiles.Should().NotContain(p => p.ChampionId == Ahri && p.Source == ChampionDamageProfileSources.Fallback,
            "a measured champion is never also a fallback");
    }

    [Fact]
    public async Task DamageProfiles_ClassifyAFixtureTeam_ExactlyAsTheDraftAxisEvaluatorDoes()
    {
        await SeedAsync();

        var response = await GetAsync();

        // Kai'Sa off-role (champion-wide entry), Garen from the older patch, Zed unmeasured.
        (int ChampionId, string Position)[] enemies =
            [(Garen, "TOP"), (KaiSa, "JUNGLE"), (Ahri, "MIDDLE"), (Zed, "BOTTOM"), (Leona, "UTILITY")];

        // The client's side: look up (champion, lane), else the champion-wide entry; keep the
        // measured ones; weight the magic share by damage per game.
        var resolved = enemies
            .Select(enemy => response.Profiles.FirstOrDefault(p => p.ChampionId == enemy.ChampionId && p.Position == enemy.Position)
                ?? response.Profiles.FirstOrDefault(p => p.ChampionId == enemy.ChampionId && p.Position == null))
            .Where(profile => profile?.Source == ChampionDamageProfileSources.Measured)
            .Select(profile => profile!)
            .ToList();
        var clientShare = resolved.Sum(p => p.MagicShare!.Value * p.DamagePerGame!.Value)
            / resolved.Sum(p => p.DamagePerGame!.Value);

        await using var db = _fixture.CreateDbContext();
        var snapshot = await ChampionProfileSnapshotRules.LoadAsync(db, Patch, CancellationToken.None);
        var facts = enemies.Select(enemy => snapshot.Find(enemy.ChampionId, enemy.Position)).ToList();
        var side = new DraftSide([.. facts.OfType<ChampionProfileFacts>()], facts.Count(f => f is null));

        clientShare.Should().BeApproximately(DraftAxisEvaluator.DamageWeightedShare(side.Facts, f => f.MagicShare), 1e-9);

        var thresholds = new DraftAxisThresholds();
        var clientBucket = clientShare < thresholds.EnemyMagicShareLow
            ? ItemContextBucket.Low
            : clientShare >= thresholds.EnemyMagicShareHigh ? ItemContextBucket.High : ItemContextBucket.Mid;
        var axes = DraftAxisEvaluator.Evaluate(new DraftContext(side, new DraftSide([], 0), null, null), thresholds);
        axes[ItemContextAxis.EnemyMagicDamage].Should().Be(clientBucket);
    }

    [Fact]
    public async Task DamageProfiles_AnswerEmpty_WhenNothingIsProfiled()
    {
        var response = await GetAsync();

        response.Patch.Should().BeNull();
        response.Profiles.Should().BeEmpty();
    }

    private async Task<ChampionDamageProfilesResponse> GetAsync()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        var response = await client.GetAsync("/champions/damage-profiles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ChampionDamageProfilesResponse>())!;
    }

    private async Task SeedAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionProfileStats.AddRange(
            Profile(KaiSa, "BOTTOM", Patch, 1_000, physical: 13_000, magic: 9_000),
            Profile(Ahri, "MIDDLE", Patch, 500, physical: 2_000, magic: 16_000),
            Profile(Garen, "TOP", OlderPatch, 400, physical: 14_000, magic: 0),
            Profile(Leona, "UTILITY", Patch, 300, physical: 2_000, magic: 4_000),
            Profile(Zed, "MIDDLE", Patch, 50, physical: 20_000, magic: 0));

        db.ChampionDamageProfileStats.AddRange(
            Build(KaiSa, "BOTTOM", ItemArchetype.OnHit, 600, physical: 17_000, magic: 4_000),
            Build(KaiSa, "BOTTOM", ItemArchetype.AbilityPower, 400, physical: 7_000, magic: 16_000),
            Build(Ahri, "MIDDLE", ItemArchetype.AbilityPower, 500, physical: 2_000, magic: 16_000));

        await db.SaveChangesAsync();
    }

    private static ChampionProfileStat Profile(int championId, string position, string patch, int games, long physical, long magic)
        => new()
        {
            ChampionId = championId,
            Position = position,
            Patch = patch,
            Games = games,
            Wins = games / 2,
            GameDurationSecondsSum = games * 1_800L,
            PhysicalDamageToChampionsSum = games * physical,
            MagicDamageToChampionsSum = games * magic,
            TrueDamageToChampionsSum = games * 2_000L,
            ItemGames = games,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static ChampionDamageProfileStat Build(
        int championId, string position, ItemArchetype archetype, int games, long physical, long magic)
        => new()
        {
            ChampionId = championId,
            Position = position,
            Patch = Patch,
            Archetype = archetype,
            Games = games,
            PhysicalDamageToChampionsSum = games * physical,
            MagicDamageToChampionsSum = games * magic,
            TrueDamageToChampionsSum = games * 1_000L,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    /// <summary>API factory whose champion statics never reach Data Dragon.</summary>
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChampionStaticsProvider>();
                services.AddSingleton<IChampionStaticsProvider>(new FakeChampionStaticsProvider());
            });
        }
    }

    private sealed class FakeChampionStaticsProvider : IChampionStaticsProvider
    {
        public Task<IReadOnlyDictionary<int, ChampionStatics>> GetChampionsAsync(string gameVersion, CancellationToken ct)
        {
            IReadOnlyDictionary<int, ChampionStatics> statics = new Dictionary<int, ChampionStatics>
            {
                [Ahri] = new(Ahri, "Ahri", 550, AttackRating: 3, MagicRating: 8),
                [Zed] = new(Zed, "Zed", 125, AttackRating: 9, MagicRating: 1),
                [Unrated] = new(Unrated, "Unrated", 125),
            };
            return Task.FromResult(statics);
        }
    }
}
