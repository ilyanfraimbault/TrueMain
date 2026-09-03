using AwesomeAssertions;
using Data.DataQuality;
using Data.Entities;
using Ingestor.Processes.Components.PatternAggregation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The guarantee behind #1418: a champion dimension cannot hold the same thing twice,
/// whatever the writer does. Every assertion here is about the database refusing a row —
/// the writer's own normalisation is asserted separately, in the unit tests, because a
/// writer that stays correct is a habit and a constraint that holds is a guarantee.
///
/// <para>
/// This is the test the #911 rune-page split would have failed, and the one the starter
/// baskets split across two price generations would have failed too.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionDimensionIdentityIntegrationTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task RunePageDimension_RejectsAPageItAlreadyHolds()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        db.ChampionDimRunePages.Add(BuildRunePage(secondary1: 8444, secondary2: 8451));
        await db.SaveChangesAsync();

        var duplicate = await InsertRawRunePageAsync(secondary1: 8444, secondary2: 8451);

        duplicate.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task RunePageDimension_RejectsThePermutationOfAPageItAlreadyHolds_EvenWithoutTheCheck()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        db.ChampionDimRunePages.Add(BuildRunePage(secondary1: 8444, secondary2: 8451));
        await db.SaveChangesAsync();

        // With the CHECK in place a swapped page never reaches the index — it is refused
        // for being stored the player's way round. Dropping it is what shows the index is
        // itself a complete guard: the two lines of defence are independent, and it is the
        // index that makes #911 unrepeatable rather than merely unfashionable.
        await db.Database.ExecuteSqlRawAsync(
            $"""
            ALTER TABLE champion_dim_rune_pages
                DROP CONSTRAINT "{ChampionDimensionCanonicalKeys.RunePageCanonicalCheckName}";
            """);

        var swapped = await InsertRawRunePageAsync(secondary1: 8451, secondary2: 8444);

        swapped.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task RunePageDimension_RejectsAPageStoredInThePlayersOrder()
    {
        await _fixture.ResetDatabaseAsync();

        // Nothing to collide with: this one is refused by the CHECK, so the state that
        // makes the reader mint a second row cannot be reached in the first place.
        var nonCanonical = await InsertRawRunePageAsync(secondary1: 8451, secondary2: 8444);

        nonCanonical.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task SpellPairDimension_RejectsBothTheSwapAndTheUnsortedRow()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        db.ChampionDimSpellPairs.Add(new ChampionDimSpellPair { Spell1Id = 4, Spell2Id = 14 });
        await db.SaveChangesAsync();

        var swapped = await InsertRawAsync(
            """INSERT INTO champion_dim_spell_pairs ("Id", "Spell1Id", "Spell2Id") VALUES (gen_random_uuid(), 14, 4)""");
        var unsorted = await InsertRawAsync(
            """INSERT INTO champion_dim_spell_pairs ("Id", "Spell1Id", "Spell2Id") VALUES (gen_random_uuid(), 21, 7)""");

        swapped.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        unsorted.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task StarterItemsDimension_RejectsTheSameBasketInAnotherOrder()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems { StarterItems = [1055, 2003, 2003] });
        await db.SaveChangesAsync();

        // The re-priced basket: same items, the display order a later patch produces.
        var reordered = await InsertRawAsync(
            """
            INSERT INTO champion_dim_starter_items ("Id", "StarterItems")
            VALUES (gen_random_uuid(), '[2003,2003,1055]')
            """);

        reordered.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task StarterItemsDimension_KeepsMultiplicityAndKeepsTheEmptyBasketSingle()
    {
        await _fixture.ResetDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems { StarterItems = [2003, 1055] });
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems { StarterItems = [2003, 2003, 1055] });
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems { StarterItems = [] });
        await db.SaveChangesAsync();

        // Two potions is not one potion: the key is the basket as a multiset.
        var keys = await db.ChampionDimStarterItems.AsNoTracking()
            .Select(row => row.CanonicalKey)
            .ToListAsync();
        keys.Should().BeEquivalentTo(["1055-2003", "1055-2003-2003", ""]);

        // The empty basket aggregates to NULL without the coalesce, and a UNIQUE index
        // lets any number of NULLs through — the one hole this guard could have had.
        var secondEmpty = await InsertRawAsync(
            """INSERT INTO champion_dim_starter_items ("Id", "StarterItems") VALUES (gen_random_uuid(), '[]')""");

        secondEmpty.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Resolver_ReturnsTheExistingRow_WhenAskedForItsPermutation()
    {
        await _fixture.ResetDatabaseAsync();

        var resolver = new ChampionDimensionResolver(new TestDbContextFactory(_fixture));

        var first = await resolver.ResolveAsync(
            [BuildIntent(secondary1: 8444, secondary2: 8451, spell1: 4, spell2: 14, starters: [1055, 2003])],
            CancellationToken.None);

        // The same three dimensions asked for the other way round. The content types
        // normalise, so this resolves to the rows already there instead of attempting an
        // insert the database would reject.
        var second = await resolver.ResolveAsync(
            [BuildIntent(secondary1: 8451, secondary2: 8444, spell1: 14, spell2: 4, starters: [2003, 1055])],
            CancellationToken.None);

        second.RunePages.Values.Single().Should().Be(first.RunePages.Values.Single());
        second.SpellPairs.Values.Single().Should().Be(first.SpellPairs.Values.Single());
        second.StarterItems.Values.Single().Should().Be(first.StarterItems.Values.Single());

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionDimRunePages.CountAsync()).Should().Be(1);
        (await db.ChampionDimSpellPairs.CountAsync()).Should().Be(1);
        (await db.ChampionDimStarterItems.CountAsync()).Should().Be(1);
    }

    private static PatternIntent BuildIntent(
        int secondary1,
        int secondary2,
        int spell1,
        int spell2,
        IReadOnlyList<int> starters)
        => new(
            ScopeId: Guid.NewGuid(),
            Build: new BuildDimensionContent(3006, 6672, 3153, 3036, 0, 0, 0, 0),
            RunePage: new RunePageDimensionContent(
                8400, 8437, 8446, 8473, 8451, 8300, secondary1, secondary2, 5008, 5008, 5001),
            SkillOrderKey: "Q-W-E",
            SpellPair: new SpellPairDimensionContent(spell1, spell2),
            StarterItemsKey: string.Join("-", starters.Order()),
            StarterItems: starters,
            Games: 1,
            Wins: 1);

    private static ChampionDimRunePage BuildRunePage(int secondary1, int secondary2) => new()
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

    private Task<PostgresException> InsertRawRunePageAsync(int secondary1, int secondary2)
        => InsertRawAsync(
            $"""
            INSERT INTO champion_dim_rune_pages (
                "Id", "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id", "PrimaryPerk2Id",
                "PrimaryPerk3Id", "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id",
                "StatOffense", "StatFlex", "StatDefense")
            VALUES (gen_random_uuid(), 8400, 8437, 8446, 8473, 8451, 8300, {secondary1}, {secondary2}, 5008, 5008, 5001)
            """);

    /// <summary>
    /// Writes the row Postgres has to refuse, bypassing EF entirely: the point is what the
    /// schema does with a row no writer of ours would produce.
    /// </summary>
    private async Task<PostgresException> InsertRawAsync(string sql)
    {
        await using var db = _fixture.CreateDbContext();

        var insert = async () => await db.Database.ExecuteSqlRawAsync(sql);

        return (await insert.Should().ThrowAsync<PostgresException>()).Which;
    }
}
