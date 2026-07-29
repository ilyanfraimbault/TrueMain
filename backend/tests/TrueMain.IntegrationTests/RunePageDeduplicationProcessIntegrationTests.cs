using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The #911 merge is almost entirely SQL against real constraints — the patterns'
/// six-column unique index and the <c>RESTRICT</c> FK from patterns to rune pages are
/// exactly what makes the statement order load-bearing — so it can only be verified
/// against Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RunePageDeduplicationProcessIntegrationTests
{
    // The pair from the issue: Overgrowth (8451) and Second Wind (8444), stored in
    // both click orders.
    private const int PerkLow = 8444;
    private const int PerkHigh = 8451;

    private readonly PostgresFixture _fixture;

    public RunePageDeduplicationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_MergesAPermutationPair_AndSumsTheSplitGamesBackTogether()
    {
        await _fixture.ResetDatabaseAsync();
        var seed = await SeedAsync();

        // The same rune page under both orders, each holding half of the real sample —
        // the split this bug caused. Everything else about the two pattern rows is
        // identical, so repointing them collides on the unique index and they must be
        // folded rather than moved.
        var canonical = await AddRunePageAsync(PerkLow, PerkHigh);
        var permuted = await AddRunePageAsync(PerkHigh, PerkLow);
        await AddPatternAsync(seed, canonical, games: 30, wins: 18);
        await AddPatternAsync(seed, permuted, games: 20, wins: 9);

        var summary = await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var pages = await db.ChampionDimRunePages.AsNoTracking().ToListAsync();
        pages.Should().ContainSingle("the permutation pair is one page, not two");
        pages[0].SecondaryPerk1Id.Should().Be(PerkLow, "the survivor is stored in canonical order");
        pages[0].SecondaryPerk2Id.Should().Be(PerkHigh);

        var patterns = await db.ChampionAggregatePatterns.AsNoTracking().ToListAsync();
        patterns.Should().ContainSingle();
        patterns[0].Games.Should().Be(50, "the split halves are added back together");
        patterns[0].Wins.Should().Be(27);
        patterns[0].RunePageId.Should().Be(pages[0].Id);

        summary.Should().BeOfType<Ingestor.Processes.Summaries.RunePageDeduplicationSummary>();
    }

    [Fact]
    public async Task RunAsync_RepointsAPatternThatHasNoCounterpart_WithoutTouchingItsCounts()
    {
        await _fixture.ResetDatabaseAsync();
        var seed = await SeedAsync();

        // Only the permuted page carries a pattern row, so there is nothing to fold
        // into — it must simply move to the survivor.
        var canonical = await AddRunePageAsync(PerkLow, PerkHigh);
        var permuted = await AddRunePageAsync(PerkHigh, PerkLow);
        await AddPatternAsync(seed, permuted, games: 14, wins: 7);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var patterns = await db.ChampionAggregatePatterns.AsNoTracking().ToListAsync();
        patterns.Should().ContainSingle();
        patterns[0].Games.Should().Be(14, "a repoint must not change the sample");
        patterns[0].Wins.Should().Be(7);
        patterns[0].RunePageId.Should().Be(canonical, "the lowest id in the group survives");
    }

    [Fact]
    public async Task RunAsync_NormalizesANonDuplicatedPageStillHoldingClickOrder()
    {
        await _fixture.ResetDatabaseAsync();
        var seed = await SeedAsync();

        // Never duplicated, so nothing to merge — but left in the player's order the
        // reader's canonical lookup would miss it and mint a second row, re-creating
        // the duplication. It must be rewritten in place.
        var permutedOnly = await AddRunePageAsync(PerkHigh, PerkLow);
        await AddPatternAsync(seed, permutedOnly, games: 9, wins: 5);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var page = await db.ChampionDimRunePages.AsNoTracking().SingleAsync();
        page.Id.Should().Be(permutedOnly, "normalisation rewrites in place, it does not re-key the row");
        page.SecondaryPerk1Id.Should().Be(PerkLow);
        page.SecondaryPerk2Id.Should().Be(PerkHigh);

        var pattern = await db.ChampionAggregatePatterns.AsNoTracking().SingleAsync();
        pattern.Games.Should().Be(9, "a normalisation touches no counts");
        pattern.RunePageId.Should().Be(permutedOnly);
    }

    [Fact]
    public async Task RunAsync_LeavesAnAlreadyCanonicalPageCompletelyUntouched()
    {
        await _fixture.ResetDatabaseAsync();
        var seed = await SeedAsync();

        var canonical = await AddRunePageAsync(PerkLow, PerkHigh);
        await AddPatternAsync(seed, canonical, games: 12, wins: 6);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var page = await db.ChampionDimRunePages.AsNoTracking().SingleAsync();
        page.Id.Should().Be(canonical);
        page.SecondaryPerk1Id.Should().Be(PerkLow);
        page.SecondaryPerk2Id.Should().Be(PerkHigh);
        (await db.ChampionAggregatePatterns.AsNoTracking().SingleAsync()).Games.Should().Be(12);
    }

    [Fact]
    public async Task RunAsync_IsANoOpOnASecondRun()
    {
        await _fixture.ResetDatabaseAsync();
        var seed = await SeedAsync();

        var canonical = await AddRunePageAsync(PerkLow, PerkHigh);
        var permuted = await AddRunePageAsync(PerkHigh, PerkLow);
        await AddPatternAsync(seed, canonical, games: 30, wins: 18);
        await AddPatternAsync(seed, permuted, games: 20, wins: 9);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        // The counts must not be folded twice — the second run has nothing to merge.
        (await db.ChampionDimRunePages.CountAsync()).Should().Be(1);
        var pattern = await db.ChampionAggregatePatterns.AsNoTracking().SingleAsync();
        pattern.Games.Should().Be(50);
        pattern.Wins.Should().Be(27);
    }

    [Fact]
    public async Task RunAsync_KeepsPagesApartWhenOnlyTheSecondaryStyleDiffers()
    {
        await _fixture.ResetDatabaseAsync();
        var seed = await SeedAsync();

        // Same perk pair, different secondary tree: genuinely different pages, and the
        // canonical key includes the style, so they must both survive. Guards against a
        // merge keyed on the perks alone.
        var resolve = await AddRunePageAsync(PerkLow, PerkHigh, secondaryStyleId: 8400);
        var sorcery = await AddRunePageAsync(PerkHigh, PerkLow, secondaryStyleId: 8200);
        await AddPatternAsync(seed, resolve, games: 10, wins: 5);
        await AddPatternAsync(seed, sorcery, games: 8, wins: 3, spell1Id: 4, spell2Id: 14);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var pages = await db.ChampionDimRunePages.AsNoTracking().OrderBy(p => p.SecondaryStyleId).ToListAsync();
        pages.Should().HaveCount(2);
        pages.Select(page => page.SecondaryStyleId).Should().Equal(8200, 8400);
        // Both still normalised, neither merged away.
        pages.Should().AllSatisfy(page =>
        {
            page.SecondaryPerk1Id.Should().Be(PerkLow);
            page.SecondaryPerk2Id.Should().Be(PerkHigh);
        });
        (await db.ChampionAggregatePatterns.CountAsync()).Should().Be(2);
    }

    private RunePageDeduplicationProcess CreateProcess()
        => new(
            NullLogger<RunePageDeduplicationProcess>.Instance,
            new TestDbContextFactory(_fixture));

    /// <summary>
    /// Seeds the account, scope and the non-rune dimensions every pattern row needs, so
    /// each test only has to vary the rune pages.
    /// </summary>
    private async Task<SeedIds> SeedAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("RuneDedupe")
            .WithTagLine("KR1")
            .WithPuuid("rune-dedupe-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        var scope = new ChampionAggregateScope
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            ChampionId = 157,
            GameVersion = "16.4",
            PlatformId = "KR",
            QueueId = 420,
            Position = "MIDDLE",
            EloBracket = EloBracket.Gold,
            Games = 50,
            Wins = 27,
            LastGameStartTimeUtc = DateTime.UtcNow,
            AggregatedAtUtc = DateTime.UtcNow,
        };
        db.ChampionAggregateScopes.Add(scope);

        var build = new ChampionDimBuild { Id = Guid.NewGuid(), BootsItemId = 3006, BuildItem0 = 6673 };
        var skillOrder = new ChampionDimSkillOrder { Id = Guid.NewGuid(), SkillOrderKey = "QWEQ" };
        var starters = new ChampionDimStarterItems
        {
            Id = Guid.NewGuid(),
            StarterItemsKey = "1055-2003",
            StarterItems = [1055, 2003],
        };
        db.ChampionDimBuilds.Add(build);
        db.ChampionDimSkillOrders.Add(skillOrder);
        db.ChampionDimStarterItems.Add(starters);

        await db.SaveChangesAsync();

        return new SeedIds(scope.Id, build.Id, skillOrder.Id, starters.Id);
    }

    private async Task<Guid> AddRunePageAsync(int secondaryPerk1Id, int secondaryPerk2Id, int secondaryStyleId = 8400)
    {
        await using var db = _fixture.CreateDbContext();

        var page = new ChampionDimRunePage
        {
            Id = Guid.NewGuid(),
            PrimaryStyleId = 8000,
            PrimaryKeystoneId = 8008,
            PrimaryPerk1Id = 8009,
            PrimaryPerk2Id = 9105,
            PrimaryPerk3Id = 8299,
            SecondaryStyleId = secondaryStyleId,
            SecondaryPerk1Id = secondaryPerk1Id,
            SecondaryPerk2Id = secondaryPerk2Id,
            StatOffense = 5005,
            StatFlex = 5008,
            StatDefense = 5001,
        };

        db.ChampionDimRunePages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    /// <summary>
    /// Adds a pattern row for a rune page. The spell pair is a parameter because it is
    /// the cheapest way to make two pattern rows differ on the unique index when a test
    /// needs them NOT to collide.
    /// </summary>
    private async Task AddPatternAsync(
        SeedIds seed,
        Guid runePageId,
        int games,
        int wins,
        int spell1Id = 4,
        int spell2Id = 12)
    {
        await using var db = _fixture.CreateDbContext();

        var spellPair = await db.ChampionDimSpellPairs
            .FirstOrDefaultAsync(pair => pair.Spell1Id == spell1Id && pair.Spell2Id == spell2Id);
        if (spellPair is null)
        {
            spellPair = new ChampionDimSpellPair { Id = Guid.NewGuid(), Spell1Id = spell1Id, Spell2Id = spell2Id };
            db.ChampionDimSpellPairs.Add(spellPair);
        }

        db.ChampionAggregatePatterns.Add(new ChampionAggregatePattern
        {
            Id = Guid.NewGuid(),
            ScopeId = seed.ScopeId,
            BuildId = seed.BuildId,
            RunePageId = runePageId,
            SkillOrderId = seed.SkillOrderId,
            SpellPairId = spellPair.Id,
            StarterItemsId = seed.StarterItemsId,
            Games = games,
            Wins = wins,
        });

        await db.SaveChangesAsync();
    }

    private readonly record struct SeedIds(Guid ScopeId, Guid BuildId, Guid SkillOrderId, Guid StarterItemsId);
}
