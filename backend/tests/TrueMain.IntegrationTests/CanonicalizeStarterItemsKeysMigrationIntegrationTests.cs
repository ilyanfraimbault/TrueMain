using Data.Entities;
using Data.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Locks the merge logic of the CanonicalizeStarterItemsKeys data migration:
/// running it on data that mimics the pre-fix state must collapse same-item
/// baskets into a single dim row, redirect FK references in
/// <c>champion_aggregate_patterns</c>, sum Games/Wins when redirection would
/// otherwise violate the (Scope, Build, Runes, Skills, Spells, Starters)
/// unique index, and re-key stale singletons whose stored key isn't yet
/// canonical (price-desc, id-asc).
/// </summary>
public sealed class CanonicalizeStarterItemsKeysMigrationIntegrationTests : IClassFixture<PostgresFixture>
{
    // Deterministic GUIDs so we can predict which dim row wins each merge
    // (the migration picks MIN(Id) as the canonical row of each duplicate
    // group). Smaller numeric suffix → smaller GUID.
    private static readonly Guid DimA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DimB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DimC = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid DimD = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private readonly PostgresFixture _fixture;

    public CanonicalizeStarterItemsKeysMigrationIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Canonicalize_MergesDuplicateDims_RedirectsPatterns_SumsCollidingTuples_AndReKeysStaleSingles()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder().Build();
        db.RiotAccounts.Add(account);

        // Three independent scopes — the collision tuple lives entirely
        // within scope1 (both P1 and P2 point at it), scope2 carries the
        // simple-redirect case, scope3 the stale-single re-key path, scope4
        // the already-canonical no-op path.
        var scope1 = BuildScope(account.Id, championId: 22, position: "BOTTOM");
        var scope2 = BuildScope(account.Id, championId: 22, position: "TOP");
        var scope3 = BuildScope(account.Id, championId: 23, position: "BOTTOM");
        var scope4 = BuildScope(account.Id, championId: 24, position: "BOTTOM");
        db.ChampionAggregateScopes.AddRange(scope1, scope2, scope3, scope4);

        var build = BuildBuild(bootsId: 3006);
        var runes = BuildRunePage(keystoneId: 8005);
        var skill = new ChampionDimSkillOrder { SkillOrderKey = "Q-W-E" };
        var spells = new ChampionDimSpellPair { Spell1Id = 4, Spell2Id = 7 };
        db.ChampionDimBuilds.Add(build);
        db.ChampionDimRunePages.Add(runes);
        db.ChampionDimSkillOrders.Add(skill);
        db.ChampionDimSpellPairs.Add(spells);

        // dimA + dimB: same basket [1055, 2003] but stored in different
        // purchase orders — these are the duplicates the migration must
        // collapse. dimA wins (lower GUID).
        var dimA = new ChampionDimStarterItems
        {
            Id = DimA,
            StarterItemsKey = "1055-2003",
            StarterItems = [1055, 2003]
        };
        var dimB = new ChampionDimStarterItems
        {
            Id = DimB,
            StarterItemsKey = "2003-1055",
            StarterItems = [2003, 1055]
        };

        // dimC: a singleton whose stored key isn't canonical (3865 at 400g
        // should come before 2003 at 50g). No merge, just a key/items
        // rewrite by the final pass.
        var dimC = new ChampionDimStarterItems
        {
            Id = DimC,
            StarterItemsKey = "2003-3865",
            StarterItems = [2003, 3865]
        };

        // dimD: already-canonical singleton. Migration must leave it alone.
        var dimD = new ChampionDimStarterItems
        {
            Id = DimD,
            StarterItemsKey = "3865-2003-2003",
            StarterItems = [3865, 2003, 2003]
        };

        db.ChampionDimStarterItems.AddRange(dimA, dimB, dimC, dimD);

        // P1 + P2: same (scope, build, runes, skill, spells) but different
        // StarterItemsId. After redirecting P2 → dimA, the unique index
        // would explode unless the migration sums their Games/Wins and
        // deletes one of them.
        var p1 = BuildPattern(scope1, build, runes, skill, spells, dimA, games: 5, wins: 2);
        var p2 = BuildPattern(scope1, build, runes, skill, spells, dimB, games: 3, wins: 1);

        // P3: simple redirect. dimB is a loser, P3 has no collision in
        // scope2, so it just flips its FK to dimA.
        var p3 = BuildPattern(scope2, build, runes, skill, spells, dimB, games: 2, wins: 1);

        // P4 + P5: untouched starter dim rows. Their FKs and counts should
        // be identical after the migration.
        var p4 = BuildPattern(scope3, build, runes, skill, spells, dimC, games: 1, wins: 0);
        var p5 = BuildPattern(scope4, build, runes, skill, spells, dimD, games: 1, wins: 1);

        db.ChampionAggregatePatterns.AddRange(p1, p2, p3, p4, p5);
        await db.SaveChangesAsync();

        var p1Id = p1.Id;
        var p2Id = p2.Id;
        var p3Id = p3.Id;
        var p4Id = p4.Id;
        var p5Id = p5.Id;

        await db.Database.ExecuteSqlRawAsync(CanonicalizeStarterItemsKeys.CanonicalizeSql);

        await using var verify = _fixture.CreateDbContext();

        var remainingDims = await verify.ChampionDimStarterItems
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .ToListAsync();
        remainingDims.Should().HaveCount(3, "dimB should have been merged into dimA");
        remainingDims.Select(d => d.Id).Should().NotContain(DimB);

        var dimAAfter = remainingDims.Single(d => d.Id == DimA);
        dimAAfter.StarterItemsKey.Should().Be("1055-2003");
        dimAAfter.StarterItems.Should().Equal(1055, 2003);

        var dimCAfter = remainingDims.Single(d => d.Id == DimC);
        dimCAfter.StarterItemsKey.Should().Be("3865-2003");
        dimCAfter.StarterItems.Should().Equal(3865, 2003);

        var dimDAfter = remainingDims.Single(d => d.Id == DimD);
        dimDAfter.StarterItemsKey.Should().Be("3865-2003-2003");
        dimDAfter.StarterItems.Should().Equal(3865, 2003, 2003);

        var patternsInScope1 = await verify.ChampionAggregatePatterns
            .AsNoTracking()
            .Where(p => p.ScopeId == scope1.Id)
            .ToListAsync();
        patternsInScope1.Should().HaveCount(1, "P2 should have been folded into P1 to satisfy the unique index");
        var survivor = patternsInScope1.Single();
        survivor.StarterItemsId.Should().Be(DimA);
        survivor.Games.Should().Be(8, "P1.Games (5) + P2.Games (3)");
        survivor.Wins.Should().Be(3, "P1.Wins (2) + P2.Wins (1)");
        new[] { p1Id, p2Id }.Should().Contain(survivor.Id, "the keeper must be one of the original two rows");

        var p3After = await verify.ChampionAggregatePatterns
            .AsNoTracking()
            .SingleAsync(p => p.Id == p3Id);
        p3After.StarterItemsId.Should().Be(DimA, "no collision in scope2 — straight FK redirect");
        p3After.Games.Should().Be(2);
        p3After.Wins.Should().Be(1);

        var p4After = await verify.ChampionAggregatePatterns
            .AsNoTracking()
            .SingleAsync(p => p.Id == p4Id);
        p4After.StarterItemsId.Should().Be(DimC);
        p4After.Games.Should().Be(1);
        p4After.Wins.Should().Be(0);

        var p5After = await verify.ChampionAggregatePatterns
            .AsNoTracking()
            .SingleAsync(p => p.Id == p5Id);
        p5After.StarterItemsId.Should().Be(DimD);
        p5After.Games.Should().Be(1);
        p5After.Wins.Should().Be(1);
    }

    [Fact]
    public async Task Canonicalize_IsIdempotent_WhenReRunAgainstAlreadyCanonicalData()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder().Build();
        db.RiotAccounts.Add(account);

        var scope = BuildScope(account.Id, championId: 22, position: "BOTTOM");
        db.ChampionAggregateScopes.Add(scope);

        var build = BuildBuild(bootsId: 3006);
        var runes = BuildRunePage(keystoneId: 8005);
        var skill = new ChampionDimSkillOrder { SkillOrderKey = "Q-W-E" };
        var spells = new ChampionDimSpellPair { Spell1Id = 4, Spell2Id = 7 };
        var dim = new ChampionDimStarterItems
        {
            Id = DimA,
            StarterItemsKey = "1055-2003",
            StarterItems = [1055, 2003]
        };
        db.ChampionDimBuilds.Add(build);
        db.ChampionDimRunePages.Add(runes);
        db.ChampionDimSkillOrders.Add(skill);
        db.ChampionDimSpellPairs.Add(spells);
        db.ChampionDimStarterItems.Add(dim);

        var pattern = BuildPattern(scope, build, runes, skill, spells, dim, games: 7, wins: 4);
        db.ChampionAggregatePatterns.Add(pattern);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(CanonicalizeStarterItemsKeys.CanonicalizeSql);
        await db.Database.ExecuteSqlRawAsync(CanonicalizeStarterItemsKeys.CanonicalizeSql);

        await using var verify = _fixture.CreateDbContext();
        var dims = await verify.ChampionDimStarterItems.AsNoTracking().ToListAsync();
        dims.Should().ContainSingle();
        dims[0].StarterItemsKey.Should().Be("1055-2003");
        dims[0].StarterItems.Should().Equal(1055, 2003);

        var pat = await verify.ChampionAggregatePatterns.AsNoTracking().SingleAsync();
        pat.StarterItemsId.Should().Be(DimA);
        pat.Games.Should().Be(7);
        pat.Wins.Should().Be(4);
    }

    private static ChampionAggregateScope BuildScope(Guid riotAccountId, int championId, string position)
        => new()
        {
            Id = Guid.NewGuid(),
            RiotAccountId = riotAccountId,
            ChampionId = championId,
            GameVersion = "16.4",
            PlatformId = "KR",
            QueueId = 420,
            Position = position,
            Games = 0,
            Wins = 0,
            LastGameStartTimeUtc = DateTime.UtcNow,
            AggregatedAtUtc = DateTime.UtcNow
        };

    private static ChampionDimBuild BuildBuild(int bootsId)
        => new()
        {
            BootsItemId = bootsId,
            BuildItem0 = 6672,
            BuildItem1 = 3094,
            BuildItem2 = 3031,
            BuildItem3 = 0,
            BuildItem4 = 0,
            BuildItem5 = 0,
            BuildItem6 = 3340
        };

    private static ChampionDimRunePage BuildRunePage(int keystoneId)
        => new()
        {
            PrimaryStyleId = 8000,
            PrimaryKeystoneId = keystoneId,
            PrimaryPerk1Id = 9111,
            PrimaryPerk2Id = 9104,
            PrimaryPerk3Id = 8014,
            SecondaryStyleId = 8400,
            SecondaryPerk1Id = 8429,
            SecondaryPerk2Id = 8451,
            StatOffense = 5005,
            StatFlex = 5008,
            StatDefense = 5011
        };

    private static ChampionAggregatePattern BuildPattern(
        ChampionAggregateScope scope,
        ChampionDimBuild build,
        ChampionDimRunePage runes,
        ChampionDimSkillOrder skill,
        ChampionDimSpellPair spells,
        ChampionDimStarterItems starters,
        int games,
        int wins)
        => new()
        {
            Id = Guid.NewGuid(),
            ScopeId = scope.Id,
            BuildId = build.Id,
            RunePageId = runes.Id,
            SkillOrderId = skill.Id,
            SpellPairId = spells.Id,
            StarterItemsId = starters.Id,
            Games = games,
            Wins = wins
        };
}
