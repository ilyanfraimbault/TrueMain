using AwesomeAssertions;
using Data;
using Data.DataQuality;
using Data.Entities;
using Data.Migrations;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Locks the one-shot backfill in <c>EnforceChampionDimensionCanonicalIdentity</c> (#1418):
/// the merge that has to collapse every split dimension row before the UNIQUE indexes can
/// be built. It runs the migration's own statements, not a re-typed copy of them.
///
/// <para>
/// Production shape at the time of writing: 419 starter rows in 17 split groups, holding
/// 100 199 of the 412 500 pattern rows. The two ways a pattern can move are what the
/// assertions are about — folded into a row the survivor already had, or simply repointed
/// — because getting the first one wrong is silent: the games do not disappear, they just
/// stop being counted where the winrate is read.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionDimensionMergeMigrationIntegrationTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task Merge_FoldsTheSplitBasketsGamesOntoOneRow_AndLeavesNoOrphanPattern()
    {
        await _fixture.ResetDatabaseAsync();
        await using var guards = await _fixture.SuspendChampionDimensionGuardsAsync();

        await using var db = _fixture.CreateDbContext();
        var scope = SeedScope(db);
        var build = SeedBuild(db);
        var runes = SeedRunePage(db, secondary1: 8444, secondary2: 8451);
        var skill = SeedSkillOrder(db);
        var spells = SeedSpellPair(db, spell1: 4, spell2: 14);

        // The same basket, stored under the two price generations that split it.
        var survivor = new ChampionDimStarterItems { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), StarterItems = [1055, 2003] };
        var loser = new ChampionDimStarterItems { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), StarterItems = [2003, 1055] };
        db.ChampionDimStarterItems.AddRange(survivor, loser);

        // Same six-column key apart from the basket: these two have to become one row
        // carrying 30 games, not two rows each showing half the picks.
        db.ChampionAggregatePatterns.Add(Pattern(scope, build, runes, skill, spells, survivor, games: 10, wins: 6));
        db.ChampionAggregatePatterns.Add(Pattern(scope, build, runes, skill, spells, loser, games: 20, wins: 9));

        // A loser-only combo: nothing to fold into, so it is repointed as it stands.
        var otherBuild = SeedBuild(db, bootsId: 3020);
        db.ChampionAggregatePatterns.Add(Pattern(scope, otherBuild, runes, skill, spells, loser, games: 5, wins: 1));

        await db.SaveChangesAsync();

        await RunMergeAsync(db);

        var dimensions = await db.ChampionDimStarterItems.AsNoTracking().ToListAsync();
        dimensions.Select(row => row.Id).Should().BeEquivalentTo([survivor.Id]);

        var patterns = await db.ChampionAggregatePatterns.AsNoTracking().ToListAsync();
        patterns.Should().AllSatisfy(pattern => pattern.StarterItemsId.Should().Be(survivor.Id));

        var folded = patterns.Single(pattern => pattern.BuildId == build.Id);
        folded.Games.Should().Be(30);
        folded.Wins.Should().Be(15);

        var repointed = patterns.Single(pattern => pattern.BuildId == otherBuild.Id);
        repointed.Games.Should().Be(5);
        repointed.Wins.Should().Be(1);
    }

    [Fact]
    public async Task Merge_CollapsesAGroupLargerThanAPair()
    {
        await _fixture.ResetDatabaseAsync();
        await using var guards = await _fixture.SuspendChampionDimensionGuardsAsync();

        await using var db = _fixture.CreateDbContext();
        var scope = SeedScope(db);
        var build = SeedBuild(db);
        var runes = SeedRunePage(db, secondary1: 8444, secondary2: 8451);
        var skill = SeedSkillOrder(db);
        var spells = SeedSpellPair(db, spell1: 4, spell2: 14);

        // Three spellings of one basket. The rune-page repair could assume pairs — the old
        // eleven-column index bounded a group at two rows — and the starter dimension never
        // had that bound, so the merge sums across all losers in one pass rather than
        // repointing them one by one into each other's unique key.
        var rows = new[]
        {
            new ChampionDimStarterItems { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), StarterItems = [1055, 2003, 2003] },
            new ChampionDimStarterItems { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), StarterItems = [2003, 1055, 2003] },
            new ChampionDimStarterItems { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), StarterItems = [2003, 2003, 1055] }
        };
        db.ChampionDimStarterItems.AddRange(rows);

        foreach (var row in rows)
        {
            db.ChampionAggregatePatterns.Add(Pattern(scope, build, runes, skill, spells, row, games: 7, wins: 3));
        }

        await db.SaveChangesAsync();

        await RunMergeAsync(db);

        var survivors = await db.ChampionDimStarterItems.AsNoTracking().ToListAsync();
        survivors.Select(row => row.Id).Should().BeEquivalentTo([rows[0].Id]);

        var pattern = await db.ChampionAggregatePatterns.AsNoTracking().SingleAsync();
        pattern.Games.Should().Be(21);
        pattern.Wins.Should().Be(9);
    }

    [Fact]
    public async Task Merge_IsANoOp_WhenNothingIsSplit()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        var scope = SeedScope(db);
        var build = SeedBuild(db);
        var runes = SeedRunePage(db, secondary1: 8444, secondary2: 8451);
        var skill = SeedSkillOrder(db);
        var spells = SeedSpellPair(db, spell1: 4, spell2: 14);
        var starters = new ChampionDimStarterItems { StarterItems = [1055, 2003] };
        db.ChampionDimStarterItems.Add(starters);
        db.ChampionAggregatePatterns.Add(Pattern(scope, build, runes, skill, spells, starters, games: 4, wins: 2));
        await db.SaveChangesAsync();

        // The steady state, and the state every environment is in after the first run:
        // the merge has to leave a healthy dimension exactly as it found it.
        await RunMergeAsync(db);

        (await db.ChampionDimStarterItems.AsNoTracking().CountAsync()).Should().Be(1);
        var pattern = await db.ChampionAggregatePatterns.AsNoTracking().SingleAsync();
        pattern.Games.Should().Be(4);
        pattern.Wins.Should().Be(2);
    }

    /// <summary>
    /// Runs the merge exactly as the migration does — same statements, same order, one
    /// transaction — for the starter dimension, the one production had split.
    /// </summary>
    private static async Task RunMergeAsync(TrueMainDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        foreach (var statement in EnforceChampionDimensionCanonicalIdentity.MergeStatements(
                     "champion_dim_starter_items",
                     ChampionDimensionCanonicalKeys.StarterItemsCanonicalKeySql,
                     "StarterItemsId"))
        {
            await db.Database.ExecuteSqlRawAsync(statement);
        }

        await transaction.CommitAsync();
    }

    private static ChampionAggregateScope SeedScope(TrueMainDbContext db)
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
        return scope;
    }

    private static ChampionDimBuild SeedBuild(TrueMainDbContext db, int bootsId = 3006)
    {
        var build = new ChampionDimBuild
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
        db.ChampionDimBuilds.Add(build);
        return build;
    }

    private static ChampionDimRunePage SeedRunePage(TrueMainDbContext db, int secondary1, int secondary2)
    {
        var page = new ChampionDimRunePage
        {
            PrimaryStyleId = 8400,
            PrimaryKeystoneId = 8437,
            PrimaryPerk1Id = 8446,
            PrimaryPerk2Id = 8473,
            PrimaryPerk3Id = 8451,
            SecondaryStyleId = 8300,
            SecondaryPerk1Id = secondary1,
            SecondaryPerk2Id = secondary2,
            StatOffense = 5008,
            StatFlex = 5008,
            StatDefense = 5001
        };
        db.ChampionDimRunePages.Add(page);
        return page;
    }

    private static ChampionDimSkillOrder SeedSkillOrder(TrueMainDbContext db)
    {
        var skill = new ChampionDimSkillOrder { SkillOrderKey = "Q-W-E" };
        db.ChampionDimSkillOrders.Add(skill);
        return skill;
    }

    private static ChampionDimSpellPair SeedSpellPair(TrueMainDbContext db, int spell1, int spell2)
    {
        var pair = new ChampionDimSpellPair { Spell1Id = spell1, Spell2Id = spell2 };
        db.ChampionDimSpellPairs.Add(pair);
        return pair;
    }

    private static ChampionAggregatePattern Pattern(
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
