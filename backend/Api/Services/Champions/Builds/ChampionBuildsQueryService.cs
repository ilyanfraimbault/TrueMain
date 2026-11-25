using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Builds;

public interface IChampionBuildsQueryService
{
    /// <summary>
    /// Returns the top builds for a champion at a given patch + position,
    /// each build keyed by (first completed item, primary keystone) and
    /// carrying the four UI sections — core, variations, build tree, rune
    /// pages. <see langword="null"/> means the champion has no aggregated
    /// data on the active queue (404 territory).
    /// </summary>
    /// <param name="scope">
    /// The slice to build. A null <see cref="ChampionScope.Patch"/> resolves the newest
    /// patch whose lane holds <c>ChampionsList:MinDefaultPatchGames</c> (#2249), or the
    /// player's floor under a player narrowing; a null <see cref="ChampionScope.Position"/>
    /// resolves the dominant position, and <see cref="LanePositions.AllPositions"/> folds
    /// every position at the resolved patch (player-scoped callers only). A narrow
    /// <see cref="ChampionScope.EloBracket"/> recomputes the builds / skill order / win rate
    /// from the selected tier(s) only and reports its <see cref="ChampionResponse.EloCoverage"/>.
    /// <see cref="ChampionPopulation.Everyone"/> widens the build from the champion's mains
    /// (the only population the aggregate held before #1346) to every tracked player.
    /// </param>
    /// <param name="player">
    /// Optional player narrowing. When omitted the response aggregates the
    /// global pool; when supplied every aggregate is computed only from that
    /// player's games on the champion. The
    /// <see cref="ChampionBuildsScope.MinGames"/> value only *prefers* a patch
    /// with enough games when resolving which to render — it no longer gates:
    /// a thinly-played champion still returns a (low-confidence) build. Only a
    /// total absence of aggregated data yields <see langword="null"/> (404).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChampionResponse?> GetAsync(
        ChampionScope scope,
        ChampionBuildsScope? player = null,
        CancellationToken ct = default);
}

public sealed class ChampionBuildsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsListOptions,
    IChampionReadCache cache)
    : IChampionBuildsQueryService
{
    public Task<ChampionResponse?> GetAsync(
        ChampionScope scope,
        ChampionBuildsScope? player = null,
        CancellationToken ct = default)
        // This read had no cache at all before #1368, and it is not a cheap one: it
        // loads the scope's patterns and walks the build tree for every tab the page
        // mounts. The player narrowing is part of the key because it narrows the rows.
        => cache.GetOrComputeAsync(
            $"champions:builds:{scope.CacheToken()}:{player?.CacheToken() ?? "default"}",
            token => ComputeAsync(
                scope.ChampionId,
                scope.Patch,
                scope.Position,
                player,
                scope.EloBracket,
                scope.Population == ChampionPopulation.Truemains,
                token),
            ct);

    private async Task<ChampionResponse?> ComputeAsync(
        int championId,
        string? patch,
        string? position,
        ChampionBuildsScope? scope,
        string? eloBracket,
        bool truemainsOnly,
        CancellationToken ct)
    {
        // A blank / ALL filter resolves to null (every tier); a bare tier to a
        // single bucket; a TIER_PLUS filter to that tier and the ones above it;
        // an unrecognised value to an empty, non-null set (matches nothing,
        // never every tier). The loader reads exactly this set.
        var bracketFilter = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var resolvedBracket = EloBracket.Normalize(eloBracket) ?? EloBracket.All;
        var isAllBracket = bracketFilter is null;

        var scopes = await ChampionScopeLoader.LoadAsync(
            db, (int)options.Value.QueueId, championId, patch, position, ct,
            riotAccountId: scope?.RiotAccountId,
            platformId: scope?.PlatformId,
            minGames: scope?.MinGames,
            eloBrackets: bracketFilter,
            truemainsOnly: truemainsOnly,
            defaultPatchMinGames: championsListOptions.Value.MinDefaultPatchGames);
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
        var allPositions = string.Equals(position, LanePositions.AllPositions, StringComparison.Ordinal);
        var allBracketGames = isAllBracket
            ? totalGames
            : await CountAllBracketGamesAsync(
                scopes[0], allPositions, scope?.RiotAccountId, scope?.PlatformId, truemainsOnly, ct);
        var coverage = RateMath.Rate(totalGames, allBracketGames);

        // A champion the profile lists as a main must never dead-end on click:
        // the min-games floor (scope.MinGames) now only *prefers* a patch with
        // enough games (see ChampionScopeLoader) — it no longer gates. Whatever
        // aggregate the player actually has renders, flagged low-confidence via
        // MinSampleMet when the sample is thin, rather than 404-ing as if they
        // never played it. A genuine 404 still comes from an absent scope
        // (LoadAsync → null above): no timeline-complete ranked game on record.
        var resolvedPatch = scopes.First().GameVersion;
        // The slice spans several positions under ALL, so no single scope names it.
        var resolvedPosition = allPositions ? LanePositions.AllPositions : scopes.First().Position;

        ChampionResponse BuildResponse(IReadOnlyList<ChampionBuildReadModel> builds) => new()
        {
            ChampionId = championId,
            Patch = resolvedPatch,
            Position = resolvedPosition,
            EloBracket = resolvedBracket,
            EloCoverage = coverage,
            MinSampleMet = totalGames >= championsListOptions.Value.MinBuildSampleGames,
            TotalGames = totalGames,
            TotalWins = totalWins,
            Builds = builds
        };

        if (totalGames == 0 || rows.Count == 0)
        {
            return BuildResponse([]);
        }

        var perGroupAggregates = ChampionBuildDistributions.Fold(rows, totalGames);
        if (perGroupAggregates.Count == 0)
        {
            return BuildResponse([]);
        }

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
            .Select(ga => ChampionBuildDistributions.Materialize(
                ga, totalGames,
                dimSpellPairs, dimSkillOrders, dimStarterItems, dimRunePages))
            .ToList();

        return BuildResponse(builds);
    }

    /// <summary>
    /// Total games across every persisted bracket for the same resolved scope
    /// (champion, patch, platform, queue, position) as <paramref name="reference"/> —
    /// every position when <paramref name="allPositions"/>, as the numerator spans them.
    /// Mirrors the loader's account filter — global callers span every account,
    /// player-scoped callers pin the one account — so the denominator matches
    /// the numerator's population. Sums scope-level totals only (no pattern
    /// join), so it's a cheap denominator for bracket coverage.
    /// </summary>
    private async Task<int> CountAllBracketGamesAsync(
        ChampionAggregateScope reference,
        bool allPositions,
        Guid? riotAccountId,
        string? platformId,
        bool truemainsOnly,
        CancellationToken ct)
        => await db.ChampionAggregateScopes
            .AsNoTracking()
            .WithBuilds(db)
            .WhereChampionScope(
                reference.ChampionId,
                reference.QueueId,
                riotAccountId,
                reference.GameVersion,
                platformId,
                allPositions ? null : reference.Position,
                // Same population as the numerator, or the coverage ratio compares
                // a mains-only slice against everyone and reads far too small.
                truemainsOnly: truemainsOnly)
            .SumAsync(s => s.Games, ct);

    private async Task<IReadOnlyList<ChampionBuildDistributions.ChampionPatternEnrichedRow>> FetchRowsAsync(
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
            .Select(row => new ChampionBuildDistributions.ChampionPatternEnrichedRow(
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

    private static List<Guid> UniqueIds(IEnumerable<Guid> source)
        => source.Distinct().ToList();
}
