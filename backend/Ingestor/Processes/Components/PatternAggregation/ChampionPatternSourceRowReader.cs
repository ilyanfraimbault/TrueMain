using Core.Lol.Map;
using Core.Lol.Patches;
using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternSourceRowReader(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    private const int MinimumAggregatedGameDurationSeconds = 15 * 60;

    // The aggregation is chunked one champion at a time so the in-memory working
    // set is bounded by a single champion's match rows rather than the whole
    // live-patch table. Materialising every match_participant (each carrying its
    // full item/skill timeline) at once ballooned the managed heap to ~6 GB and
    // got the process OOM-killed. The (patch, platform) live keys are computed
    // once by the caller and threaded into each per-champion load.
    internal async Task<IReadOnlyList<int>> LoadChampionIdsAsync(
        int queueId,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Champions with at least one "main" account — a superset of the champions
        // that can produce source rows. The per-champion source query re-applies
        // the full IsMain + queue + timeline filter, so a champion with no
        // qualifying rows just yields a cheap empty iteration. Derived from
        // main_champion_stats (small: one row per tracked account/champion)
        // rather than a DISTINCT over the match_participants 3-way join, which
        // scanned the whole table and hit the 300s command timeout now that
        // parallel query is disabled (max_parallel_workers_per_gather=0, #589).
        var mainChampionIds = await db.MainChampionStats
            .AsNoTracking()
            .Where(stat => stat.IsMain)
            .Select(stat => stat.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        // Union champions that already have aggregate scopes, so a champion that
        // lost all its qualifying rows still gets a pass and has its stale
        // live-patch scopes pruned (replace-by-scope down to zero).
        var scopeChampionIds = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId)
            .Select(scope => scope.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        return mainChampionIds.Union(scopeChampionIds).ToList();
    }

    internal async Task<IReadOnlySet<(string GameVersion, string PlatformId)>> LoadLivePatchKeysAsync(
        int queueId,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await LoadLivePatchKeysCoreAsync(db, queueId, ct);
    }

    internal async Task<ChampionPatternAggregationInputs> LoadAggregationInputsAsync(
        int queueId,
        int championId,
        IReadOnlySet<(string GameVersion, string PlatformId)> livePatchKeys,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var existingAggregateScopes = LoadExistingAggregateScopes(
            await LoadExistingScopeKeysAsync(db, queueId, championId, ct),
            livePatchKeys);
        var sourceRows = await LoadSourceRowsAsync(db, queueId, championId, ct);

        return new ChampionPatternAggregationInputs
        {
            ExistingAggregateScopes = existingAggregateScopes,
            SourceRows = sourceRows
        };
    }

    private static async Task<List<AggregateScopeKey>> LoadExistingScopeKeysAsync(
        TrueMainDbContext db,
        int queueId,
        int championId,
        CancellationToken ct)
        => await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId && scope.ChampionId == championId)
            .Select(scope => new AggregateScopeKey(
                scope.ChampionId,
                scope.GameVersion,
                scope.PlatformId,
                scope.QueueId))
            .Distinct()
            .ToListAsync(ct);

    private static List<AggregateScopeKey> LoadExistingAggregateScopes(
        IReadOnlyCollection<AggregateScopeKey> existingScopes,
        IReadOnlySet<(string GameVersion, string PlatformId)> livePatchKeys)
        // Cleanup keys are the scopes the persister will delete-and-rebuild.
        // We must only ever touch scopes for patches whose match data is still
        // present: MatchDataRetention purges match_participants/matches beyond
        // the last few patches, so once a patch's matches are gone there are no
        // source rows left to rebuild its scopes. Including such a patch in the
        // cleanup set would delete its aggregates permanently (the "everything
        // before patch X vanished" bug). Restrict the cleanup to live patches —
        // (patch, platform) pairs that still have matches for this queue — so
        // older patches keep their frozen aggregates while live patches retain
        // the full replace-by-scope semantics (stale scopes still get pruned).
        => existingScopes
            .Where(scope => livePatchKeys.Contains((scope.GameVersion, scope.PlatformId)))
            .ToList();

    private static async Task<HashSet<(string GameVersion, string PlatformId)>> LoadLivePatchKeysCoreAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        // matches.GameVersion is the raw Riot version (e.g. "16.5.2"); scopes
        // store the normalised patch ("16.5"), so normalise before comparing.
        // NormalizeGameVersion is C# (PatchVersion.Parse) that EF can't translate
        // to SQL, hence the materialise-then-normalise in memory. The result set
        // is the distinct (version, platform) pairs in `matches`, which retention
        // keeps bounded to a handful of patches — small enough to fold client-side.
        var rawPatchKeys = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .Select(match => new { match.GameVersion, match.PlatformId })
            .Distinct()
            .ToListAsync(ct);

        return rawPatchKeys
            .Select(key => (NormalizeGameVersion(key.GameVersion), key.PlatformId))
            .ToHashSet();
    }

    private static async Task<List<AggregateSourceRow>> LoadSourceRowsAsync(
        TrueMainDbContext db,
        int queueId,
        int championId,
        CancellationToken ct)
    {
        var sourceRows = await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            join stat in db.MainChampionStats.AsNoTracking()
                on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                equals new { stat.PlatformId, stat.Puuid, stat.ChampionId }
            where stat.IsMain
                && participant.ChampionId == championId
                && participant.RiotAccountId != null
                && match.QueueId == queueId
                && match.TimelineIngested
            select new AggregateSourceRow
            {
                MatchId = match.Id,
                ParticipantId = participant.ParticipantId,
                ChampionId = participant.ChampionId,
                GameVersion = NormalizeGameVersion(match.GameVersion),
                PlatformId = match.PlatformId,
                QueueId = match.QueueId,
                GameStartTimeUtc = match.GameStartTimeUtc,
                GameDurationSeconds = match.GameDurationSeconds,
                RiotAccountId = participant.RiotAccountId!.Value,
                Win = participant.Win,
                Kills = participant.Kills,
                Deaths = participant.Deaths,
                Assists = participant.Assists,
                Position = LolPositionExtensions.Parse(participant.TeamPosition).ToRiotString(),
                Summoner1Id = participant.Summoner1Id,
                Summoner2Id = participant.Summoner2Id,
                PrimaryStyleId = participant.PrimaryStyleId,
                SubStyleId = participant.SubStyleId,
                PerksOffense = participant.PerksOffense,
                PerksFlex = participant.PerksFlex,
                PerksDefense = participant.PerksDefense,
                ItemEvents = participant.ItemEvents,
                SkillEvents = participant.SkillEvents,
                Item0 = participant.Item0,
                Item1 = participant.Item1,
                Item2 = participant.Item2,
                Item3 = participant.Item3,
                Item4 = participant.Item4,
                Item5 = participant.Item5,
                Item6 = participant.Item6
            })
            .ToListAsync(ct);

        var filtered = sourceRows
            .Where(HasCompleteCorrelatedTimeline)
            .ToList();

        await HydratePerkSelectionsAsync(db, filtered, ct);
        await HydrateEloBracketsAsync(db, filtered, ct);
        return filtered;
    }

    /// <summary>
    /// Buckets each source row by the player's ranked tier <em>at game time</em>:
    /// the <c>rank_snapshots</c> capture nearest (in absolute time) to the
    /// match's <c>GameStartTimeUtc</c> for that account. Rows whose account has
    /// no snapshot keep the default <see cref="EloBracket.Unranked"/> bucket.
    ///
    /// The nearest-capture match is a LINQ join between the rows and the
    /// candidate snapshots (keyed on the account), then a per-row reduction to
    /// the minimum <c>|CapturedAtUtc - GameStartTimeUtc|</c>. Snapshots are
    /// loaded once for the involved accounts and folded in memory — mirroring
    /// <see cref="HydratePerkSelectionsAsync"/> — because a per-row correlated
    /// "nearest" subquery would not translate to an efficient single SQL pass.
    /// </summary>
    private static async Task HydrateEloBracketsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<AggregateSourceRow> sourceRows,
        CancellationToken ct)
    {
        if (sourceRows.Count == 0)
        {
            return;
        }

        var accountIds = sourceRows.Select(row => row.RiotAccountId).Distinct().ToList();

        var snapshots = await db.RankSnapshots
            .AsNoTracking()
            .Where(snapshot => accountIds.Contains(snapshot.RiotAccountId))
            .Select(snapshot => new { snapshot.RiotAccountId, snapshot.CapturedAtUtc, snapshot.Tier })
            .ToListAsync(ct);

        var snapshotsByAccount = snapshots
            .GroupBy(snapshot => snapshot.RiotAccountId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<(DateTime, string?)>)group
                    .Select(snapshot => (snapshot.CapturedAtUtc, (string?)snapshot.Tier))
                    .ToList());

        foreach (var row in sourceRows)
        {
            if (!snapshotsByAccount.TryGetValue(row.RiotAccountId, out var accountSnapshots))
            {
                // No snapshot for this account → already UNRANKED by default.
                continue;
            }

            // Shared nearest-capture resolution so this and the match_participants
            // enrichment pass bucket every game identically.
            row.EloBracket = EloBracketResolver.FromNearestSnapshot(accountSnapshots, row.GameStartTimeUtc);
        }
    }

    private static string NormalizeGameVersion(string gameVersion)
        => PatchVersion.TryParse(gameVersion, out var patch) ? patch.ToMajorMinor() : gameVersion;

    private static async Task HydratePerkSelectionsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<AggregateSourceRow> sourceRows,
        CancellationToken ct)
    {
        if (sourceRows.Count == 0)
        {
            return;
        }

        var matchIds = sourceRows.Select(row => row.MatchId).Distinct().ToList();

        var perkRows = await (
            from selection in db.ParticipantPerkSelections.AsNoTracking()
            join catalog in db.PerkSelectionCatalogs.AsNoTracking()
                on selection.PerkSelectionCatalogId equals catalog.Id
            where matchIds.Contains(selection.MatchId)
            select new
            {
                selection.MatchId,
                selection.ParticipantId,
                catalog.SelectionIndex,
                catalog.PerkId,
                catalog.StyleDescription
            })
            .ToListAsync(ct);

        var perksByParticipant = perkRows
            .GroupBy(row => (row.MatchId, row.ParticipantId))
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var row in sourceRows)
        {
            if (!perksByParticipant.TryGetValue((row.MatchId, row.ParticipantId), out var perks))
            {
                continue;
            }

            var primary = perks
                .Where(perk => string.Equals(perk.StyleDescription, "primaryStyle", StringComparison.OrdinalIgnoreCase))
                .OrderBy(perk => perk.SelectionIndex)
                .ToList();
            var secondary = perks
                .Where(perk => string.Equals(perk.StyleDescription, "subStyle", StringComparison.OrdinalIgnoreCase))
                .OrderBy(perk => perk.SelectionIndex)
                .ToList();

            row.PrimaryKeystoneId = primary.ElementAtOrDefault(0)?.PerkId ?? 0;
            row.PrimaryPerk1Id = primary.ElementAtOrDefault(1)?.PerkId ?? 0;
            row.PrimaryPerk2Id = primary.ElementAtOrDefault(2)?.PerkId ?? 0;
            row.PrimaryPerk3Id = primary.ElementAtOrDefault(3)?.PerkId ?? 0;
            row.SecondaryPerk1Id = secondary.ElementAtOrDefault(0)?.PerkId ?? 0;
            row.SecondaryPerk2Id = secondary.ElementAtOrDefault(1)?.PerkId ?? 0;
        }
    }

    private static bool HasCompleteCorrelatedTimeline(AggregateSourceRow row)
    {
        var purchaseCount = row.ItemEvents.Count(itemEvent =>
            itemEvent.ItemId > 0
            && itemEvent.EventType.Equals("ITEM_PURCHASED", StringComparison.OrdinalIgnoreCase));
        var normalSkillLevelUps = row.SkillEvents.Count(skillEvent =>
            skillEvent.LevelUpType.Equals("NORMAL", StringComparison.OrdinalIgnoreCase));

        return purchaseCount > 0
            && row.GameDurationSeconds >= MinimumAggregatedGameDurationSeconds
            && normalSkillLevelUps >= 3
            && !string.IsNullOrWhiteSpace(row.Position);
    }
}
