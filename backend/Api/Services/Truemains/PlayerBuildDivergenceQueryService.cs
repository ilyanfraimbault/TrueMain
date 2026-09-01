using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Champions;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Builds the "you vs mains" comparison by overlaying two reads of the same
/// aggregate slice: the player's <c>champion_aggregate_scope</c> rows, and the
/// rows of every <em>other</em> account on the same champion + patch + position.
/// No new table and no live match scan — the pattern rows already carry the
/// starter set, boots, completed items and skill order behind every game.
/// </summary>
public sealed class PlayerBuildDivergenceQueryService(
    TrueMainDbContext db,
    TruemainAccountResolver resolver,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<ChampionsListOptions> championsListOptions) : IPlayerBuildDivergenceQueryService
{
    public async Task<PlayerBuildDivergenceResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        // Player side first: it decides which patch + position the comparison
        // happens at (their most recent patch with a usable sample), exactly
        // like the build page they are looking at.
        var playerScopes = await ChampionScopeLoader.LoadAsync(
            db, queueId, championId, patch, position, ct,
            riotAccountId: account.Id,
            platformId: account.PlatformId,
            minGames: PlayerChampionBuildsQueryService.MinPlayerGames);
        if (playerScopes is null)
        {
            return null;
        }

        var resolvedPatch = playerScopes[0].GameVersion;
        var resolvedPosition = playerScopes[0].Position;

        // Mains side pinned to the slice the player resolved to, so the two are
        // comparable by construction. Deliberately not narrowed to the player's
        // platform: a main is a main whatever their region, and a wider pool is
        // what makes "x% of mains" mean anything.
        //
        // Queried directly rather than through ChampionScopeLoader: the loader's
        // job is to *resolve* an unspecified patch / position, and it spends a
        // first round trip doing that. Here both are already known — they came
        // off a scope row above, so they are canonical by construction — which
        // makes that pass pure overhead. The player's own scopes are excluded in
        // SQL so "x% of mains" is never partly "x% of you"; on a champion only
        // this player mains, the reference comes back empty and the card says so.
        var mainsScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                championId,
                queueId,
                riotAccountId: null,
                resolvedPatch,
                platformId: null,
                resolvedPosition)
            .Where(scope => scope.RiotAccountId != account.Id)
            .ToListAsync(ct);

        var playerScopeIds = playerScopes.Select(scope => scope.Id).ToHashSet();
        var mainsScopeIds = mainsScopes.Select(scope => scope.Id).ToList();

        var playerGames = playerScopes.Sum(scope => scope.Games);
        var mainsGames = mainsScopes.Sum(scope => scope.Games);
        var mainsPlayers = mainsScopes.Select(scope => scope.RiotAccountId).Distinct().Count();

        var minPlayerGames = PlayerChampionBuildsQueryService.MinPlayerGames;

        // The reference pool is judged against the same configured floor as any other
        // champion build slice (ChampionsList:MinBuildSampleGames) — the two used to be
        // two hard-coded 20s calling each other mirrors, with nothing keeping them equal.
        var minMainsGames = championsListOptions.Value.MinBuildSampleGames;
        var minSampleMet = playerGames >= minPlayerGames;
        var referenceSampleMet = mainsGames >= minMainsGames;

        PlayerBuildDivergenceResponse BuildResponse(IReadOnlyList<BuildDivergenceReadModel> dimensions) => new()
        {
            ChampionId = championId,
            Patch = resolvedPatch,
            Position = resolvedPosition,
            PlayerGames = playerGames,
            MainsGames = mainsGames,
            MainsPlayers = mainsPlayers,
            MinPlayerGames = minPlayerGames,
            MinMainsGames = minMainsGames,
            MinSampleMet = minSampleMet,
            ReferenceSampleMet = referenceSampleMet,
            Dimensions = dimensions
        };

        // Below either floor there is nothing honest to say — return the counts
        // (so the page can show how far off the bar is) and no dimensions.
        if (!minSampleMet || !referenceSampleMet)
        {
            return BuildResponse([]);
        }

        var scopedRows = await FetchRowsAsync(playerScopeIds, mainsScopeIds, ct);
        var playerRows = scopedRows
            .Where(row => playerScopeIds.Contains(row.ScopeId))
            .Select(row => row.Row)
            .ToList();
        var mainsRows = scopedRows
            .Where(row => !playerScopeIds.Contains(row.ScopeId))
            .Select(row => row.Row)
            .ToList();

        if (playerRows.Count == 0 || mainsRows.Count == 0)
        {
            return BuildResponse([]);
        }

        var dimensions = await BuildDimensionsAsync(playerRows, mainsRows, playerGames, mainsGames, ct);
        return BuildResponse(dimensions);
    }

    /// <summary>
    /// Single round trip for both pools: the pattern rows of the union of scope
    /// ids, joined to the build dimension for the boots + completed items. The
    /// caller partitions them back by scope id (the two id sets are disjoint —
    /// the player's scopes were removed from the mains side).
    /// </summary>
    private async Task<IReadOnlyList<ScopedDivergenceRow>> FetchRowsAsync(
        IReadOnlyCollection<Guid> playerScopeIds,
        IReadOnlyCollection<Guid> mainsScopeIds,
        CancellationToken ct)
    {
        var scopeIds = playerScopeIds.Concat(mainsScopeIds).ToList();

        // EF cannot translate constructing a positional record inside a join
        // projection, so project to an anonymous type (a flat SELECT) and build
        // the records after materialisation — same shape as
        // ChampionBuildsQueryService.FetchRowsAsync.
        var raw = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Where(pattern => scopeIds.Contains(pattern.ScopeId))
            .Join(
                db.ChampionDimBuilds.AsNoTracking(),
                pattern => pattern.BuildId,
                build => build.Id,
                (pattern, build) => new
                {
                    pattern.ScopeId,
                    pattern.StarterItemsId,
                    pattern.SkillOrderId,
                    build.BootsItemId,
                    build.BuildItem0,
                    build.BuildItem1,
                    build.BuildItem2,
                    build.BuildItem3,
                    build.BuildItem4,
                    build.BuildItem5,
                    build.BuildItem6,
                    pattern.Games,
                    pattern.Wins
                })
            .ToListAsync(ct);

        return raw
            .Select(row => new ScopedDivergenceRow(
                row.ScopeId,
                new DivergencePatternRow(
                    row.StarterItemsId,
                    row.SkillOrderId,
                    row.BootsItemId,
                    row.BuildItem0,
                    row.BuildItem1,
                    row.BuildItem2,
                    row.BuildItem3,
                    row.BuildItem4,
                    row.BuildItem5,
                    row.BuildItem6,
                    row.Games,
                    row.Wins)))
            .ToList();
    }

    private async Task<IReadOnlyList<BuildDivergenceReadModel>> BuildDimensionsAsync(
        IReadOnlyList<DivergencePatternRow> playerRows,
        IReadOnlyList<DivergencePatternRow> mainsRows,
        int playerGames,
        int mainsGames,
        CancellationToken ct)
    {
        var starter = BuildKeyedDimension(
            playerRows, mainsRows, playerGames, mainsGames, row => row.StarterItemsId);
        var skillOrder = BuildKeyedDimension(
            playerRows, mainsRows, playerGames, mainsGames, row => row.SkillOrderId);
        // Games that ended without boots say nothing about boot preference, so
        // they leave the pool entirely — including the denominator, which stays
        // the slice total so the pick rate reads as "x% of your games".
        var boots = BuildKeyedDimension(
            playerRows.Where(row => row.BootsItemId > 0).ToList(),
            mainsRows.Where(row => row.BootsItemId > 0).ToList(),
            playerGames,
            mainsGames,
            row => row.BootsItemId);
        var itemPath = BuildPathDimension(playerRows, mainsRows, playerGames, mainsGames);

        // Resolve only the handful of dimension ids the two sides actually
        // landed on (at most two per keyed dimension) instead of the tables.
        var starterIds = CollectKeys(starter);
        var skillIds = CollectKeys(skillOrder);

        var starterDims = starterIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<int>>()
            : await db.ChampionDimStarterItems.AsNoTracking()
                .Where(dim => starterIds.Contains(dim.Id))
                .ToDictionaryAsync(dim => dim.Id, dim => (IReadOnlyList<int>)dim.StarterItems, ct);
        var skillDims = skillIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ChampionDimSkillOrders.AsNoTracking()
                .Where(dim => skillIds.Contains(dim.Id))
                .ToDictionaryAsync(dim => dim.Id, dim => dim.SkillOrderKey, ct);

        var dimensions = new List<BuildDivergenceReadModel>();

        AddIfRenderable(dimensions, Materialize(
            BuildDivergenceDimensions.StarterItems,
            starter,
            key => starterDims.TryGetValue(key, out var items) ? items : null,
            _ => null));
        AddIfRenderable(dimensions, Materialize(
            BuildDivergenceDimensions.Boots,
            boots,
            key => new[] { key },
            _ => null));
        AddIfRenderable(dimensions, MaterializePath(itemPath));
        AddIfRenderable(dimensions, Materialize(
            BuildDivergenceDimensions.SkillOrder,
            skillOrder,
            _ => Array.Empty<int>(),
            key => skillDims.TryGetValue(key, out var sequence) ? SplitSkillOrder(sequence) : null));

        // Most actionable first: what you do differently, and among those the
        // dimensions the mains agree on most strongly (a 40/35/25 split is far
        // weaker advice than a 90/10 one).
        return dimensions
            .OrderByDescending(dimension => dimension.Diverges)
            .ThenByDescending(dimension => dimension.Mains.PickRate)
            .ThenBy(dimension => dimension.Dimension, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddIfRenderable(
        List<BuildDivergenceReadModel> dimensions,
        BuildDivergenceReadModel? dimension)
    {
        if (dimension is not null)
        {
            dimensions.Add(dimension);
        }
    }

    private static PendingKeyedDimension<TKey>? BuildKeyedDimension<TKey>(
        IReadOnlyList<DivergencePatternRow> playerRows,
        IReadOnlyList<DivergencePatternRow> mainsRows,
        int playerGames,
        int mainsGames,
        Func<DivergencePatternRow, TKey> keySelector)
        where TKey : IComparable<TKey>, IEquatable<TKey>
    {
        var playerChoice = BuildDivergenceAnalyzer.TopChoice(playerRows, keySelector);
        var mainsChoice = BuildDivergenceAnalyzer.TopChoice(mainsRows, keySelector);
        if (playerChoice is null || mainsChoice is null)
        {
            return null;
        }

        var (games, wins) = BuildDivergenceAnalyzer.TotalsForKey(mainsRows, keySelector, playerChoice.Value.Key);

        return new PendingKeyedDimension<TKey>(
            playerChoice.Value, mainsChoice.Value, playerGames, mainsGames, games, wins);
    }

    private static PendingPathDimension? BuildPathDimension(
        IReadOnlyList<DivergencePatternRow> playerRows,
        IReadOnlyList<DivergencePatternRow> mainsRows,
        int playerGames,
        int mainsGames)
    {
        var playerPath = BuildDivergenceAnalyzer.WalkCorePath(playerRows);
        var mainsPath = BuildDivergenceAnalyzer.WalkCorePath(mainsRows);
        if (playerPath.ItemIds.Count == 0 || mainsPath.ItemIds.Count == 0)
        {
            return null;
        }

        var (games, wins) = BuildDivergenceAnalyzer.TotalsForPath(mainsRows, playerPath.ItemIds);

        return new PendingPathDimension(
            playerPath, mainsPath, playerGames, mainsGames, games, wins);
    }

    private static List<TKey> CollectKeys<TKey>(PendingKeyedDimension<TKey>? dimension)
        where TKey : IComparable<TKey>, IEquatable<TKey>
        => dimension is { } value
            ? new List<TKey> { value.Player.Key, value.Mains.Key }.Distinct().ToList()
            : [];

    /// <summary>
    /// Turns a resolved keyed dimension into its read model.
    /// <see langword="null"/> when either side's choice cannot be rendered
    /// (a dim row that no longer resolves, or a game with no recorded skill
    /// levels) — an unrenderable row is worse than an absent one.
    /// </summary>
    private static BuildDivergenceReadModel? Materialize<TKey>(
        string dimension,
        PendingKeyedDimension<TKey>? pending,
        Func<TKey, IReadOnlyList<int>?> itemResolver,
        Func<TKey, IReadOnlyList<string>?> skillResolver)
        where TKey : IComparable<TKey>, IEquatable<TKey>
    {
        if (pending is not { } value)
        {
            return null;
        }

        var playerItems = itemResolver(value.Player.Key);
        var mainsItems = itemResolver(value.Mains.Key);
        var playerSkills = skillResolver(value.Player.Key);
        var mainsSkills = skillResolver(value.Mains.Key);

        var playerRenderable = (playerItems?.Count ?? 0) > 0 || (playerSkills?.Count ?? 0) > 0;
        var mainsRenderable = (mainsItems?.Count ?? 0) > 0 || (mainsSkills?.Count ?? 0) > 0;
        if (!playerRenderable || !mainsRenderable)
        {
            return null;
        }

        return Compose(
            dimension,
            diverges: !value.Player.Key.Equals(value.Mains.Key),
            player: new BuildChoiceReadModel
            {
                ItemIds = playerItems ?? [],
                Skills = playerSkills ?? [],
                Games = value.Player.Games,
                PickRate = RateMath.Rate(value.Player.Games, value.PlayerGames),
                WinRate = RateMath.Rate(value.Player.Wins, value.Player.Games)
            },
            mains: new BuildChoiceReadModel
            {
                ItemIds = mainsItems ?? [],
                Skills = mainsSkills ?? [],
                Games = value.Mains.Games,
                PickRate = RateMath.Rate(value.Mains.Games, value.MainsGames),
                WinRate = RateMath.Rate(value.Mains.Wins, value.Mains.Games)
            },
            mainsGames: value.MainsGames,
            mainsGamesOnPlayerChoice: value.MainsGamesOnPlayerChoice,
            mainsWinsOnPlayerChoice: value.MainsWinsOnPlayerChoice);
    }

    /// <summary>
    /// Item-path counterpart of <see cref="Materialize{TKey}"/>. Two paths of
    /// different lengths count as diverging: a pool whose consensus runs one
    /// item deeper than the other's genuinely disagrees about that item.
    /// </summary>
    private static BuildDivergenceReadModel? MaterializePath(PendingPathDimension? pending)
        => pending is not { } value
            ? null
            : Compose(
                BuildDivergenceDimensions.ItemPath,
                diverges: !value.Player.ItemIds.SequenceEqual(value.Mains.ItemIds),
                player: new BuildChoiceReadModel
                {
                    ItemIds = value.Player.ItemIds,
                    Games = value.Player.Games,
                    PickRate = RateMath.Rate(value.Player.Games, value.PlayerGames),
                    WinRate = RateMath.Rate(value.Player.Wins, value.Player.Games)
                },
                mains: new BuildChoiceReadModel
                {
                    ItemIds = value.Mains.ItemIds,
                    Games = value.Mains.Games,
                    PickRate = RateMath.Rate(value.Mains.Games, value.MainsGames),
                    WinRate = RateMath.Rate(value.Mains.Wins, value.Mains.Games)
                },
                mainsGames: value.MainsGames,
                mainsGamesOnPlayerChoice: value.MainsGamesOnPlayerChoice,
                mainsWinsOnPlayerChoice: value.MainsWinsOnPlayerChoice);

    private static BuildDivergenceReadModel Compose(
        string dimension,
        bool diverges,
        BuildChoiceReadModel player,
        BuildChoiceReadModel mains,
        int mainsGames,
        int mainsGamesOnPlayerChoice,
        int mainsWinsOnPlayerChoice)
        => new()
        {
            Dimension = dimension,
            Diverges = diverges,
            Player = player,
            Mains = mains,
            MainsGamesOnPlayerChoice = mainsGamesOnPlayerChoice,
            MainsRateOnPlayerChoice = RateMath.Rate(mainsGamesOnPlayerChoice, mainsGames),
            MainsWinRateOnPlayerChoice = mainsGamesOnPlayerChoice == 0
                ? null
                : RateMath.Rate(mainsWinsOnPlayerChoice, mainsGamesOnPlayerChoice)
        };

    private static IReadOnlyList<string>? SplitSkillOrder(string skillOrderKey)
        => string.IsNullOrEmpty(skillOrderKey)
            ? null
            : skillOrderKey.Split('-', StringSplitOptions.RemoveEmptyEntries);

    private sealed record ScopedDivergenceRow(Guid ScopeId, DivergencePatternRow Row);

    private readonly record struct PendingKeyedDimension<TKey>(
        KeyedChoice<TKey> Player,
        KeyedChoice<TKey> Mains,
        int PlayerGames,
        int MainsGames,
        int MainsGamesOnPlayerChoice,
        int MainsWinsOnPlayerChoice);

    private readonly record struct PendingPathDimension(
        PathChoice Player,
        PathChoice Mains,
        int PlayerGames,
        int MainsGames,
        int MainsGamesOnPlayerChoice,
        int MainsWinsOnPlayerChoice);
}
