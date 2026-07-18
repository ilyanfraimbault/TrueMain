using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionBuildsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options)
    : IChampionBuildsQueryService
{
    private const int MaxBuilds = 4;
    private const double MinBuildPickRate = 0.05;
    private const int VariationsTopN = 3;
    private const int RunePagesTopN = 3;

    /// <summary>
    /// Below this many games a bracket slice is flagged low-confidence
    /// (<see cref="ChampionResponse.MinSampleMet"/> = false). High brackets
    /// (Master+) routinely fall below this, so it guards the UI rather than
    /// hiding the data.
    /// </summary>
    private const int MinSampleGames = 20;

    public async Task<ChampionResponse?> GetAsync(
        int championId,
        string? patch,
        string? position,
        CancellationToken ct,
        ChampionBuildsScope? scope = null,
        string? eloBracket = null)
    {
        // A blank / ALL / unrecognised filter resolves to null (every tier); a
        // bare tier to a single bucket; a TIER_PLUS filter to that tier and the
        // ones above it. The loader reads exactly this set.
        var bracketFilter = EloBracket.ResolveFilter(eloBracket);
        var resolvedBracket = EloBracket.Normalize(eloBracket) ?? EloBracket.All;
        var isAllBracket = bracketFilter is null;

        var scopes = await ChampionScopeLoader.LoadAsync(
            db, (int)options.Value.QueueId, championId, patch, position, ct,
            riotAccountId: scope?.RiotAccountId,
            platformId: scope?.PlatformId,
            minGames: scope?.MinGames,
            eloBrackets: bracketFilter);
        if (scopes is null)
        {
            return null;
        }

        var scopeIds = scopes.Select(s => s.Id).ToList();
        var rows = await FetchRowsAsync(scopeIds, ct);
        var totalGames = rows.Sum(row => row.Games);
        var totalWins = rows.Sum(row => row.Wins);

        // Coverage denominator: games across every bracket at the same resolved
        // patch + position. For the ALL bracket the slice already spans them, so
        // coverage is 1; for a narrow bracket we re-sum the scope totals without
        // the bracket filter (cheap — scope rows only, no pattern join).
        var allBracketGames = isAllBracket
            ? totalGames
            : await CountAllBracketGamesAsync(
                scopes[0], scope?.RiotAccountId, scope?.PlatformId, ct);
        var coverage = RateMath.Rate(totalGames, allBracketGames);

        // Player-scoped requests carry a minimum-games floor: a champion the
        // player has barely touched would produce a sparse, misleading build,
        // so we report "no data" (null → 404 → empty state) rather than a
        // thin payload. The floor is evaluated against the resolved
        // patch+position slice — the same denominator the page renders.
        // Global callers pass no scope, so no floor applies.
        if (scope is not null && totalGames < scope.MinGames)
        {
            return null;
        }

        var resolvedPatch = scopes.First().GameVersion;
        var resolvedPosition = scopes.First().Position;

        ChampionResponse BuildResponse(IReadOnlyList<ChampionBuildReadModel> builds) => new()
        {
            ChampionId = championId,
            Patch = resolvedPatch,
            Position = resolvedPosition,
            EloBracket = resolvedBracket,
            EloCoverage = coverage,
            MinSampleMet = totalGames >= MinSampleGames,
            TotalGames = totalGames,
            TotalWins = totalWins,
            Builds = builds
        };

        if (totalGames == 0 || rows.Count == 0)
        {
            return BuildResponse([]);
        }

        var groups = rows
            .GroupBy(row => new BuildKey(row.BuildItem0, row.PrimaryKeystoneId))
            .Select(group => new PendingBuild(
                group.Key,
                group.ToList(),
                group.Sum(row => row.Games),
                group.Sum(row => row.Wins)))
            .Where(pending => (double)pending.Games / totalGames > MinBuildPickRate)
            .OrderByDescending(pending => pending.Games)
            .ThenBy(pending => pending.Key.FirstItemId)
            .ThenBy(pending => pending.Key.PrimaryKeystoneId)
            .Take(MaxBuilds)
            .ToList();

        if (groups.Count == 0)
        {
            return BuildResponse([]);
        }

        var perGroupAggregates = groups
            .Select(pending => AggregateGroup(pending))
            .ToList();

        var spellIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopSpells.Select(t => t.Id)));
        var skillIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopSkills.Select(t => t.Id)));
        var starterIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopStarters.Select(t => t.Id)));
        var runeIds = UniqueIds(perGroupAggregates.SelectMany(ga => ga.TopRunes.Select(t => t.Id)));

        var dimSpellPairs = await db.ChampionDimSpellPairs.AsNoTracking()
            .Where(dim => spellIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimSkillOrders = await db.ChampionDimSkillOrders.AsNoTracking()
            .Where(dim => skillIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimStarterItems = await db.ChampionDimStarterItems.AsNoTracking()
            .Where(dim => starterIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimRunePages = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);

        var builds = perGroupAggregates
            .Select(ga => MaterializeBuild(
                ga, totalGames,
                dimSpellPairs, dimSkillOrders, dimStarterItems, dimRunePages))
            .ToList();

        return BuildResponse(builds);
    }

    /// <summary>
    /// Total games across every persisted bracket for the same resolved scope
    /// (champion, patch, platform, queue, position) as <paramref name="reference"/>.
    /// Mirrors the loader's account filter — global callers span every account,
    /// player-scoped callers pin the one account — so the denominator matches
    /// the numerator's population. Sums scope-level totals only (no pattern
    /// join), so it's a cheap denominator for bracket coverage.
    /// </summary>
    private async Task<int> CountAllBracketGamesAsync(
        ChampionAggregateScope reference,
        Guid? riotAccountId,
        string? platformId,
        CancellationToken ct)
        => await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                reference.ChampionId,
                reference.QueueId,
                riotAccountId,
                reference.GameVersion,
                platformId,
                reference.Position)
            .SumAsync(s => s.Games, ct);

    private async Task<IReadOnlyList<ChampionPatternEnrichedRow>> FetchRowsAsync(
        IReadOnlyList<Guid> scopeIds,
        CancellationToken ct)
    {
        // EF cannot translate construction of a positional record inside a
        // join projection, so we project into an anonymous type first (which
        // EF translates to a flat SELECT) and convert to the record after
        // materialisation.
        var raw = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Where(pattern => scopeIds.Contains(pattern.ScopeId))
            .Join(
                db.ChampionDimBuilds.AsNoTracking(),
                pattern => pattern.BuildId,
                build => build.Id,
                (pattern, build) => new { Pattern = pattern, Build = build })
            .Join(
                db.ChampionDimRunePages.AsNoTracking(),
                joined => joined.Pattern.RunePageId,
                rune => rune.Id,
                (joined, rune) => new
                {
                    joined.Pattern.SpellPairId,
                    joined.Pattern.SkillOrderId,
                    joined.Pattern.StarterItemsId,
                    joined.Pattern.RunePageId,
                    joined.Build.BuildItem0,
                    joined.Build.BuildItem1,
                    joined.Build.BuildItem2,
                    joined.Build.BuildItem3,
                    joined.Build.BuildItem4,
                    joined.Build.BuildItem5,
                    joined.Build.BuildItem6,
                    joined.Build.BootsItemId,
                    rune.PrimaryKeystoneId,
                    joined.Pattern.Games,
                    joined.Pattern.Wins
                })
            .Where(row => row.BuildItem0 > 0 && row.PrimaryKeystoneId > 0)
            .ToListAsync(ct);

        return raw
            .Select(row => new ChampionPatternEnrichedRow(
                row.SpellPairId,
                row.SkillOrderId,
                row.StarterItemsId,
                row.RunePageId,
                row.BuildItem0,
                row.BuildItem1,
                row.BuildItem2,
                row.BuildItem3,
                row.BuildItem4,
                row.BuildItem5,
                row.BuildItem6,
                row.BootsItemId,
                row.PrimaryKeystoneId,
                row.Games,
                row.Wins))
            .ToList();
    }

    private static GroupAggregates AggregateGroup(PendingBuild pending)
    {
        var sliceGames = pending.Games;
        var rows = pending.Rows;

        var topSpells = AggregateByGuid(
            rows, r => r.SpellPairId, r => r.Games, r => r.Wins, VariationsTopN);
        var topSkills = AggregateByGuid(
            rows, r => r.SkillOrderId, r => r.Games, r => r.Wins, VariationsTopN);
        var topStarters = AggregateByGuid(
            rows, r => r.StarterItemsId, r => r.Games, r => r.Wins, VariationsTopN);
        var topRunes = AggregateByGuid(
            rows, r => r.RunePageId, r => r.Games, r => r.Wins, RunePagesTopN);
        var topBoots = AggregateBoots(rows, VariationsTopN);

        // Build the (pruned) tree once and derive the highlighted item path
        // from the same tree, so anything the path includes is guaranteed to
        // be visible in the build-tree visualization (no "ghost" deep items).
        var sequences = rows
            .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                row.BuildItem1, row.BuildItem2, row.BuildItem3,
                row.BuildItem4, row.BuildItem5, row.BuildItem6,
                row.Games, row.Wins))
            .ToList();
        var buildTree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames);
        var (itemPath, itemPathGames, itemPathWins) = ChampionBuildPathAnalyzer.WalkPath(
            buildTree, pending.Key.FirstItemId, sliceGames, pending.Wins);

        return new GroupAggregates(
            pending.Key,
            pending.Rows,
            pending.Games,
            pending.Wins,
            topSpells,
            topSkills,
            topStarters,
            topRunes,
            topBoots,
            itemPath,
            itemPathGames,
            itemPathWins,
            buildTree);
    }

    private static List<DimAggregate> AggregateByGuid(
        IReadOnlyList<ChampionPatternEnrichedRow> rows,
        Func<ChampionPatternEnrichedRow, Guid> idSelector,
        Func<ChampionPatternEnrichedRow, int> gamesSelector,
        Func<ChampionPatternEnrichedRow, int> winsSelector,
        int topN)
        => rows
            .GroupBy(idSelector)
            .Select(group => new DimAggregate(
                group.Key,
                group.Sum(gamesSelector),
                group.Sum(winsSelector)))
            .OrderByDescending(aggregate => aggregate.Games)
            .ThenByDescending(aggregate => aggregate.Wins)
            .ThenBy(aggregate => aggregate.Id)
            .Take(topN)
            .ToList();

    private static List<BootsAggregate> AggregateBoots(
        IReadOnlyList<ChampionPatternEnrichedRow> rows,
        int topN)
        => rows
            .Where(row => row.BootsItemId > 0)
            .GroupBy(row => row.BootsItemId)
            .Select(group => new BootsAggregate(
                group.Key,
                group.Sum(row => row.Games),
                group.Sum(row => row.Wins)))
            .OrderByDescending(aggregate => aggregate.Games)
            .ThenByDescending(aggregate => aggregate.Wins)
            .ThenBy(aggregate => aggregate.ItemId)
            .Take(topN)
            .ToList();

    private static List<Guid> UniqueIds(IEnumerable<Guid> source)
        => source.Distinct().ToList();

    private static ChampionBuildReadModel MaterializeBuild(
        GroupAggregates aggregates,
        int totalGames,
        Dictionary<Guid, ChampionDimSpellPair> spellDims,
        Dictionary<Guid, ChampionDimSkillOrder> skillDims,
        Dictionary<Guid, ChampionDimStarterItems> starterDims,
        Dictionary<Guid, ChampionDimRunePage> runeDims)
    {
        var sliceGames = aggregates.Games;
        var spellVariations = aggregates.TopSpells
            .Select(agg => MaterializeSpell(agg, sliceGames, spellDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
        var skillVariations = aggregates.TopSkills
            .Select(agg => MaterializeSkill(agg, sliceGames, skillDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
        var starterVariations = aggregates.TopStarters
            .Select(agg => MaterializeStarter(agg, sliceGames, starterDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
        var bootsVariations = aggregates.TopBoots
            .Select(agg => MaterializeBoots(agg, sliceGames))
            .ToList();
        var runePages = aggregates.TopRunes
            .Select(agg => MaterializeRunePage(agg, sliceGames, runeDims))
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();

        var itemPath = new BuildItemPathReadModel
        {
            ItemIds = aggregates.ItemPath,
            Games = aggregates.ItemPathGames,
            PickRate = RateMath.Rate(aggregates.ItemPathGames, sliceGames),
            WinRate = RateMath.Rate(aggregates.ItemPathWins, aggregates.ItemPathGames)
        };

        return new ChampionBuildReadModel
        {
            FirstItemId = aggregates.Key.FirstItemId,
            PrimaryKeystoneId = aggregates.Key.PrimaryKeystoneId,
            Games = sliceGames,
            PickRate = RateMath.Rate(sliceGames, totalGames),
            WinRate = RateMath.Rate(aggregates.Wins, sliceGames),
            Core = new BuildCoreReadModel
            {
                ItemPath = itemPath,
                Boots = bootsVariations.FirstOrDefault(),
                StarterItems = starterVariations.FirstOrDefault(),
                SummonerSpells = spellVariations.FirstOrDefault(),
                SkillOrder = skillVariations.FirstOrDefault(),
                RunePage = runePages.FirstOrDefault()
            },
            Variations = new BuildVariationsReadModel
            {
                Boots = bootsVariations,
                StarterItems = starterVariations,
                SummonerSpells = spellVariations,
                SkillOrder = skillVariations
            },
            BuildTree = aggregates.BuildTree
                .Select(node => ChampionBuildPathAnalyzer.ToReadModel(node, sliceGames))
                .ToList(),
            RunePages = runePages
        };
    }

    private static BuildSummonerSpellsReadModel? MaterializeSpell(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimSpellPair> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        return new BuildSummonerSpellsReadModel
        {
            Spell1Id = dim.Spell1Id,
            Spell2Id = dim.Spell2Id,
            Games = aggregate.Games,
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };
    }

    private static BuildSkillOrderReadModel? MaterializeSkill(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimSkillOrder> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        var sequence = string.IsNullOrEmpty(dim.SkillOrderKey)
            ? Array.Empty<string>()
            : dim.SkillOrderKey.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return new BuildSkillOrderReadModel
        {
            Sequence = sequence,
            Games = aggregate.Games,
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };
    }

    private static BuildItemSetReadModel? MaterializeStarter(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimStarterItems> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        return new BuildItemSetReadModel
        {
            ItemIds = dim.StarterItems,
            Games = aggregate.Games,
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };
    }

    private static BuildItemSetReadModel MaterializeBoots(BootsAggregate aggregate, int sliceGames)
        => new()
        {
            ItemIds = [aggregate.ItemId],
            Games = aggregate.Games,
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };

    private static BuildRunePageReadModel? MaterializeRunePage(
        DimAggregate aggregate,
        int sliceGames,
        Dictionary<Guid, ChampionDimRunePage> dims)
    {
        if (!dims.TryGetValue(aggregate.Id, out var dim))
        {
            return null;
        }
        return new BuildRunePageReadModel
        {
            PrimaryStyleId = dim.PrimaryStyleId,
            PrimaryKeystoneId = dim.PrimaryKeystoneId,
            PrimaryPerk1Id = dim.PrimaryPerk1Id,
            PrimaryPerk2Id = dim.PrimaryPerk2Id,
            PrimaryPerk3Id = dim.PrimaryPerk3Id,
            SecondaryStyleId = dim.SecondaryStyleId,
            SecondaryPerk1Id = dim.SecondaryPerk1Id,
            SecondaryPerk2Id = dim.SecondaryPerk2Id,
            StatOffense = dim.StatOffense,
            StatFlex = dim.StatFlex,
            StatDefense = dim.StatDefense,
            Games = aggregate.Games,
            PickRate = RateMath.Rate(aggregate.Games, sliceGames),
            WinRate = RateMath.Rate(aggregate.Wins, aggregate.Games)
        };
    }

    private readonly record struct BuildKey(int FirstItemId, int PrimaryKeystoneId);

    private sealed record PendingBuild(
        BuildKey Key,
        IReadOnlyList<ChampionPatternEnrichedRow> Rows,
        int Games,
        int Wins);

    private sealed record GroupAggregates(
        BuildKey Key,
        IReadOnlyList<ChampionPatternEnrichedRow> Rows,
        int Games,
        int Wins,
        IReadOnlyList<DimAggregate> TopSpells,
        IReadOnlyList<DimAggregate> TopSkills,
        IReadOnlyList<DimAggregate> TopStarters,
        IReadOnlyList<DimAggregate> TopRunes,
        IReadOnlyList<BootsAggregate> TopBoots,
        IReadOnlyList<int> ItemPath,
        int ItemPathGames,
        int ItemPathWins,
        IReadOnlyList<ChampionBuildPathAnalyzer.TreeNode> BuildTree);

    private readonly record struct DimAggregate(Guid Id, int Games, int Wins);

    private readonly record struct BootsAggregate(int ItemId, int Games, int Wins);

    private sealed record ChampionPatternEnrichedRow(
        Guid SpellPairId,
        Guid SkillOrderId,
        Guid StarterItemsId,
        Guid RunePageId,
        int BuildItem0,
        int BuildItem1,
        int BuildItem2,
        int BuildItem3,
        int BuildItem4,
        int BuildItem5,
        int BuildItem6,
        int BootsItemId,
        int PrimaryKeystoneId,
        int Games,
        int Wins);

}
