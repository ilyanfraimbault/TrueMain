using Data;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Phase 6.1 — locks the schema invariants of the new junction tables in
/// the database, before any aggregator code uses them. The next PRs in
/// the phase rely on these contracts (UNIQUE-driven get-or-create,
/// cascade vs restrict semantics) so they're worth pinning explicitly.
/// </summary>
public sealed class ChampionPatternJunctionSchemaIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string UniqueViolationSqlState = "23505";
    private const string ForeignKeyViolationSqlState = "23503";

    private readonly PostgresFixture _fixture;

    public ChampionPatternJunctionSchemaIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DimBuild_RejectsDuplicateContent()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        db.ChampionDimBuilds.Add(BuildBuild(bootsId: 3006));
        await db.SaveChangesAsync();

        db.ChampionDimBuilds.Add(BuildBuild(bootsId: 3006));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresState(exception, UniqueViolationSqlState);
    }

    [Fact]
    public async Task DimBuild_AcceptsDistinctContent()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        db.ChampionDimBuilds.Add(BuildBuild(bootsId: 3006));
        db.ChampionDimBuilds.Add(BuildBuild(bootsId: 3047));
        await db.SaveChangesAsync();

        var rows = await db.ChampionDimBuilds.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task DimRunePage_RejectsDuplicateContent()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        db.ChampionDimRunePages.Add(BuildRunePage(keystoneId: 8005));
        await db.SaveChangesAsync();

        db.ChampionDimRunePages.Add(BuildRunePage(keystoneId: 8005));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresState(exception, UniqueViolationSqlState);
    }

    [Theory]
    [InlineData("Q-W-E", "Q-W-E", true)]
    [InlineData("Q-W-E", "Q-E-W", false)]
    public async Task DimSkillOrder_UniquenessIsKeyedOnSkillOrderKey(string first, string second, bool expectConflict)
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        db.ChampionDimSkillOrders.Add(new ChampionDimSkillOrder { SkillOrderKey = first });
        await db.SaveChangesAsync();

        db.ChampionDimSkillOrders.Add(new ChampionDimSkillOrder { SkillOrderKey = second });

        if (expectConflict)
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            AssertPostgresState(exception, UniqueViolationSqlState);
        }
        else
        {
            await db.SaveChangesAsync();
            var rows = await db.ChampionDimSkillOrders.AsNoTracking().ToListAsync();
            rows.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Pattern_RejectsDuplicateScopeAndDimensionTuple()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        var (scope, build, runes, skill, spells, starters) = await SeedScopeAndDimensionsAsync(db);

        db.ChampionAggregatePatterns.Add(BuildPattern(scope, build, runes, skill, spells, starters));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresState(exception, UniqueViolationSqlState);
    }

    [Fact]
    public async Task Pattern_CascadesDeleteFromScope()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        var (scope, _, _, _, _, _) = await SeedScopeAndDimensionsAsync(db);

        db.ChampionAggregateScopes.Remove(scope);
        await db.SaveChangesAsync();

        var remainingPatterns = await db.ChampionAggregatePatterns.AsNoTracking().CountAsync();
        remainingPatterns.Should().Be(0);

        // The dim row stays — it's a global reference, not scoped.
        var stillThereDimBuilds = await db.ChampionDimBuilds.AsNoTracking().CountAsync();
        stillThereDimBuilds.Should().Be(1);
    }

    [Fact]
    public async Task DimBuild_RestrictsDeleteWhilePatternReferencesIt()
    {
        await _fixture.ResetDatabaseAsync();

        Guid buildId;
        await using (var seedDb = _fixture.CreateDbContext())
        {
            var (_, build, _, _, _, _) = await SeedScopeAndDimensionsAsync(seedDb);
            buildId = build.Id;
        }

        // Fresh context so the EF change tracker has no in-memory pattern
        // row to "fix up" before sending the DELETE. Without this, EF
        // fails locally with InvalidOperationException about a severed
        // required relationship — which proves nothing about the DB-level
        // FK constraint we actually want to lock down.
        await using var deleteDb = _fixture.CreateDbContext();
        var act = async () => await deleteDb.ChampionDimBuilds
            .Where(b => b.Id == buildId)
            .ExecuteDeleteAsync();

        var exception = await Assert.ThrowsAnyAsync<Exception>(act);
        var pg = FindPostgresException(exception);
        pg.Should().NotBeNull("the delete should fail with a Postgres FK violation");
        pg!.SqlState.Should().Be(ForeignKeyViolationSqlState);
    }

    private static PostgresException? FindPostgresException(Exception? root)
    {
        var current = root;
        while (current is not null)
        {
            if (current is PostgresException pg)
            {
                return pg;
            }
            current = current.InnerException;
        }
        return null;
    }

    private static void AssertPostgresState(DbUpdateException exception, string expectedSqlState)
    {
        var pg = exception.InnerException as PostgresException;
        pg.Should().NotBeNull();
        pg!.SqlState.Should().Be(expectedSqlState);
    }

    private static async Task<(
        ChampionAggregateScope Scope,
        ChampionDimBuild Build,
        ChampionDimRunePage RunePage,
        ChampionDimSkillOrder SkillOrder,
        ChampionDimSpellPair SpellPair,
        ChampionDimStarterItems StarterItems)> SeedScopeAndDimensionsAsync(TrueMainDbContext db)
    {
        var account = new RiotAccountBuilder().Build();
        db.RiotAccounts.Add(account);

        var scope = new ChampionAggregateScope
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            ChampionId = 22,
            GameVersion = "16.4",
            PlatformId = "KR",
            QueueId = 420,
            Position = "BOTTOM",
            Games = 0,
            Wins = 0,
            LastGameStartTimeUtc = DateTime.UtcNow,
            AggregatedAtUtc = DateTime.UtcNow
        };
        db.ChampionAggregateScopes.Add(scope);

        var build = BuildBuild(bootsId: 3006);
        var runes = BuildRunePage(keystoneId: 8005);
        var skill = new ChampionDimSkillOrder { SkillOrderKey = "Q-W-E" };
        var spells = new ChampionDimSpellPair { Spell1Id = 4, Spell2Id = 7 };
        var starters = new ChampionDimStarterItems
        {
            StarterItemsKey = "1055-2003",
            StarterItems = [1055, 2003]
        };
        db.ChampionDimBuilds.Add(build);
        db.ChampionDimRunePages.Add(runes);
        db.ChampionDimSkillOrders.Add(skill);
        db.ChampionDimSpellPairs.Add(spells);
        db.ChampionDimStarterItems.Add(starters);

        db.ChampionAggregatePatterns.Add(BuildPattern(scope, build, runes, skill, spells, starters));

        await db.SaveChangesAsync();
        return (scope, build, runes, skill, spells, starters);
    }

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
        ChampionDimStarterItems starters)
        => new()
        {
            ScopeId = scope.Id,
            BuildId = build.Id,
            RunePageId = runes.Id,
            SkillOrderId = skill.Id,
            SpellPairId = spells.Id,
            StarterItemsId = starters.Id,
            Games = 1,
            Wins = 1
        };
}
