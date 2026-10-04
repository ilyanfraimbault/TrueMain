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
    /// <param name="championId">Riot champion id to build the page for.</param>
    /// <param name="patch">
    /// Requested patch (<c>major.minor</c> or full Riot version); when null
    /// the dominant patch in the scope is resolved automatically.
    /// </param>
    /// <param name="position">
    /// Requested Riot team position; when null the dominant position is
    /// resolved automatically.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="scope">
    /// Optional player narrowing. When omitted the response aggregates the
    /// global pool; when a player scope is supplied every aggregate is
    /// computed only from that player's games on the champion. The
    /// <see cref="ChampionBuildsScope.MinGames"/> value only *prefers* a patch
    /// with enough games when resolving which to render — it no longer gates:
    /// a thinly-played champion still returns a (low-confidence) build. Only a
    /// total absence of aggregated data yields <see langword="null"/> (404).
    /// </param>
    /// <param name="eloBracket">
    /// Optional elo filter (per <c>Core.Lol.Ranking.EloBracket</c>): <c>ALL</c>,
    /// a bare tier (e.g. <c>GOLD</c> — that tier only), or a <c>TIER_PLUS</c>
    /// form (e.g. <c>GOLD_PLUS</c> — that tier and above). When null or
    /// <c>ALL</c> the response spans every tier; otherwise it recomputes the
    /// builds / skill order / win rate from the selected tier(s) only and
    /// reports its <see cref="ChampionResponse.EloCoverage"/> /
    /// <see cref="ChampionResponse.MinSampleMet"/>.
    /// </param>
    /// <param name="truemainsOnly">
    /// When <see langword="true"/> (the default) the build is folded only from
    /// accounts that are <em>mains</em> of the champion — the site's truemain
    /// population, and the only population the aggregate held before #1346.
    /// When <see langword="false"/> it widens to every tracked player's games on
    /// the champion.
    /// </param>
    Task<ChampionResponse?> GetAsync(
        int championId,
        string? patch,
        string? position,
        ChampionBuildsScope? scope = null,
        string? eloBracket = null,
        bool truemainsOnly = true,
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
        int championId,
        string? patch,
        string? position,
        ChampionBuildsScope? scope = null,
        string? eloBracket = null,
        bool truemainsOnly = true,
        CancellationToken ct = default)
        // This read had no cache at all before #1368, and it is not a cheap one: it
        // loads the scope's patterns and walks the build tree for every tab the page
        // mounts. The scope object is part of the key because it narrows the rows.
        => cache.GetOrComputeAsync(
            $"champions:builds:{championId}:{patch ?? "auto"}:{position ?? "auto"}"
                + $":{eloBracket ?? "all"}:{(truemainsOnly ? "truemains" : "everyone")}"
                + $":{scope?.CacheToken() ?? "default"}",
            token => ComputeAsync(championId, patch, position, scope, eloBracket, truemainsOnly, token),
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
            truemainsOnly: truemainsOnly);
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
                scopes[0], scope?.RiotAccountId, scope?.PlatformId, truemainsOnly, ct);
        var coverage = RateMath.Rate(totalGames, allBracketGames);

        // A champion the profile lists as a main must never dead-end on click:
        // the min-games floor (scope.MinGames) now only *prefers* a patch with
        // enough games (see ChampionScopeLoader) — it no longer gates. Whatever
        // aggregate the player actually has renders, flagged low-confidence via
        // MinSampleMet when the sample is thin, rather than 404-ing as if they
        // never played it. A genuine 404 still comes from an absent scope
        // (LoadAsync → null above): no timeline-complete ranked game on record.
        var resolvedPatch = scopes.First().GameVersion;
        var resolvedPosition = scopes.First().Position;

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
        bool truemainsOnly,
        CancellationToken ct)
        => await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                reference.ChampionId,
                reference.QueueId,
                riotAccountId,
                reference.GameVersion,
                platformId,
                reference.Position,
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
