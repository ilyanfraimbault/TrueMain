using Core.Lol.Map;
using Core.Lol.Ranking;
using Data;
using Data.Aggregation;
using Data.BuildFacts;
using Ingestor.Processes.Components.Retention;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternSourceRowReader(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    /// <summary>
    /// The header aggregate keeps a floor of its own, strictly above
    /// <see cref="ChampionCohort.MinimumGameDurationSeconds"/>: it reads build and skill
    /// order out of a correlated timeline, and a game decided at 8 minutes has neither a
    /// build nor a skill path worth folding. So it is not a second opinion about what a
    /// remake is — it excludes every remake the shared rule does, plus the early
    /// surrenders that are games nobody finished building in. A test pins the ordering.
    /// </summary>
    internal const int MinimumAggregatedGameDurationSeconds = 15 * 60;

    /// <summary>
    /// The (patch, platform) pairs this run may rebuild aggregates for: each platform's
    /// <c>MatchDataRetention:RetainedPatchCount</c> most recent patches, the same window
    /// <c>MatchDataRetentionProcess</c> keeps raw matches for.
    ///
    /// <para>
    /// It used to be "every (patch, platform) that still has one match", which conflated
    /// two different things and broke both of them. Old games keep arriving long after
    /// their patch retired — a harvested account's history, a backfill — and retention only
    /// sweeps them on its next pass, so a retired patch was permanently flickering back
    /// into the set. Each flicker un-froze that patch and replaced its whole aggregate
    /// history with a rebuild over the handful of stragglers (#466 regressed in practice),
    /// and because this set is snapshotted once per run while the per-champion source rows
    /// are read across the following twenty minutes, a pair that flickered in mid-loop was
    /// rebuilt <em>without</em> being in the cleanup set — inserting scopes on top of the
    /// frozen ones and failing the run on a duplicate key (#1549).
    /// </para>
    ///
    /// <para>
    /// Both defects are one missing invariant: the window is a single snapshot per run, and
    /// every scope written has to come from inside it. The caller threads this set into the
    /// source-row load as well as the cleanup, so that holds by construction — a patch that
    /// enters the window mid-run is simply picked up by the next run.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlySet<(string GameVersion, string PlatformId)>> LoadLivePatchKeysAsync(
        int queueId,
        int retainedPatchCount,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var retainedPatchesByPlatform = await RetainedPatchWindow.LoadAsync(
            db, queueId, Math.Max(1, retainedPatchCount), ct);
        return RetainedPatchWindow.ToPatchPlatformPairs(retainedPatchesByPlatform);
    }

    /// <summary>
    /// Sends every match of the retained window back to the aggregation
    /// (<c>PatternAggregation:RebuildAll</c>), so this run rebuilds every live slice.
    /// </summary>
    internal async Task<int> RequeueWindowAsync(
        int queueId,
        IReadOnlySet<(string GameVersion, string PlatformId)> livePatchKeys,
        CancellationToken ct)
    {
        var windowPatches = livePatchKeys.Select(key => key.GameVersion).Distinct().ToList();
        var windowPlatforms = livePatchKeys.Select(key => key.PlatformId).Distinct().ToList();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // The cross product of the two lists can reach a retired-patch straggler; it is
        // simply flagged again without work, like any pending match outside the window.
        return await db.Matches
            .Where(match => match.QueueId == queueId
                && match.PatternAggregated
                && windowPlatforms.Contains(match.PlatformId)
                && windowPatches.Contains(match.Patch ?? match.GameVersion))
            .ExecuteUpdateAsync(setters => setters.SetProperty(match => match.PatternAggregated, false), ct);
    }

    /// <summary>
    /// The pending matches and the slices they touch (#1367). A match is pending until its
    /// timeline is in: the aggregation reads build and skill order out of it, so flagging
    /// a match before then would leave its games out for good.
    ///
    /// <para>
    /// The ids are read before the slices, on purpose. The fetch lane keeps inserting
    /// matches while this runs; one that lands in between adds a slice nobody will flag a
    /// match for — rebuilt once more next run, harmless — whereas the opposite order would
    /// flag a match whose slice was never rebuilt.
    /// </para>
    ///
    /// <para>
    /// Every tracked participant marks its slice, main or not: whether the slice holds
    /// anything is the rebuild's decision, and a demoted player's slice has to be rebuilt
    /// to empty. A pending match outside the retained window marks nothing and is flagged
    /// all the same — that patch is frozen.
    /// </para>
    /// </summary>
    internal async Task<PendingPatternWork> LoadPendingWorkAsync(
        int queueId,
        IReadOnlySet<(string GameVersion, string PlatformId)> livePatchKeys,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // IX_matches_pattern_pending: the pending tail, not the table.
        var matchIds = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId && !match.PatternAggregated && match.TimelineIngested)
            .Select(match => match.Id)
            .ToListAsync(ct);

        var windowPatches = livePatchKeys.Select(key => key.GameVersion).Distinct().ToList();
        var windowPlatforms = livePatchKeys.Select(key => key.PlatformId).Distinct().ToList();
        if (matchIds.Count == 0 || windowPatches.Count == 0)
        {
            return new PendingPatternWork(matchIds, []);
        }

        var touched = await (
            from match in db.Matches.AsNoTracking()
            join participant in db.MatchParticipants.AsNoTracking() on match.Id equals participant.MatchId
            where match.QueueId == queueId
                && !match.PatternAggregated
                && match.TimelineIngested
                && participant.RiotAccountId != null
                && windowPlatforms.Contains(match.PlatformId)
                && windowPatches.Contains(match.Patch ?? match.GameVersion)
            select new
            {
                RiotAccountId = participant.RiotAccountId!.Value,
                participant.ChampionId,
                GameVersion = match.Patch ?? match.GameVersion,
                match.PlatformId
            })
            .Distinct()
            .ToListAsync(ct);

        var slices = touched
            .Where(slice => livePatchKeys.Contains((slice.GameVersion, slice.PlatformId)))
            .Select(slice => new AggregateSliceKey(slice.RiotAccountId, slice.ChampionId, slice.GameVersion, slice.PlatformId))
            .ToHashSet();

        return new PendingPatternWork(matchIds, slices);
    }

    /// <summary>Flags the matches whose slices this run rebuilt.</summary>
    internal async Task MarkAggregatedAsync(IReadOnlyList<string> matchIds, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        foreach (var chunk in matchIds.Chunk(MarkBatchSize))
        {
            await db.Matches
                .Where(match => chunk.Contains(match.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(match => match.PatternAggregated, true), ct);
        }
    }

    private const int MarkBatchSize = 5000;

    // `includeNonMains` folds the non-main population too
    // (`MainAnalysis:AggregateNonMainPopulation`). Off by default: widening it is ~4.3x
    // the source rows on production, on the process that once OOM-killed the VPS (#601).
    //
    // Reads every retained game of the slices' accounts on the slices' champions, then
    // keeps the slices asked for: the two lists are a superset of the slice set only
    // across accounts of the same batch, which is a few rows, not a patch.
    internal async Task<List<AggregateSourceRow>> LoadSourceRowsAsync(
        int queueId,
        IReadOnlyCollection<AggregateSliceKey> slices,
        bool includeNonMains,
        CancellationToken ct)
    {
        var sliceSet = slices.ToHashSet();
        if (sliceSet.Count == 0)
        {
            return [];
        }

        var accountIds = sliceSet.Select(slice => slice.RiotAccountId).Distinct().ToList();
        var championIds = sliceSet.Select(slice => slice.ChampionId).Distinct().ToList();
        var windowPatches = sliceSet.Select(slice => slice.GameVersion).Distinct().ToList();
        var windowPlatforms = sliceSet.Select(slice => slice.PlatformId).Distinct().ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var sourceRows = await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            // LEFT JOIN, and no IsMain predicate: main-ness is carried onto the
            // row rather than used to exclude it, so the aggregate holds both
            // populations and reads choose between them. A participant the main
            // analysis has never scored yields no stat row at all and is simply
            // not a main.
            join statCandidate in db.MainChampionStats.AsNoTracking()
                on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                equals new { statCandidate.PlatformId, statCandidate.Puuid, statCandidate.ChampionId }
                into statMatches
            from stat in statMatches.DefaultIfEmpty()
            where (includeNonMains || (stat != null && stat.IsMain))
                && participant.RiotAccountId != null
                && accountIds.Contains(participant.RiotAccountId.Value)
                && championIds.Contains(participant.ChampionId)
                && match.QueueId == queueId
                && match.TimelineIngested
                && windowPlatforms.Contains(match.PlatformId)
                && windowPatches.Contains(match.Patch ?? match.GameVersion)
            select new AggregateSourceRow
            {
                MatchId = match.Id,
                ParticipantId = participant.ParticipantId,
                ChampionId = participant.ChampionId,
                // Same normalisation the live keys use, now read from the stored
                // generated column instead of computed per row on the client; the
                // raw-version fallback is the COALESCE (#1368).
                GameVersion = match.Patch ?? match.GameVersion,
                PlatformId = match.PlatformId,
                QueueId = match.QueueId,
                GameStartTimeUtc = match.GameStartTimeUtc,
                GameDurationSeconds = match.GameDurationSeconds,
                RiotAccountId = participant.RiotAccountId!.Value,
                IsMain = stat != null && stat.IsMain,
                Win = participant.Win,
                Kills = participant.Kills,
                Deaths = participant.Deaths,
                Assists = participant.Assists,
                Position = LanePositions.Normalize(participant.TeamPosition),
                EloBracket = participant.EloBracket == string.Empty ? EloBracket.Unranked : participant.EloBracket,
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
                RoleBoundItemId = participant.RoleBoundItemId
            })
            .ToListAsync(ct);

        var filtered = sourceRows
            .Where(row => sliceSet.Contains(new AggregateSliceKey(row.RiotAccountId, row.ChampionId, row.GameVersion, row.PlatformId)))
            .Where(HasCompleteCorrelatedTimeline)
            .ToList();

        await HydratePerkSelectionsAsync(db, filtered, ct);
        return filtered;
    }

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
            // Ordered by perk id, NOT by SelectionIndex like the primary tree above
            // (#911). In the primary tree the selection index coincides with the tree
            // row, so it is already canonical. In the secondary tree the player picks
            // two runes from three free rows in whatever order they like, so the index
            // is the player's click order — which meant the same rune page was stored
            // as two rows, (8451, 8444) and (8444, 8451). The 11-column unique index
            // does not catch a permutation, so the page's games and wins were split
            // across both rows, roughly halving its displayed pick rate and distorting
            // the top-N selection. On production that was 20 370 duplicate pairs, 48%
            // of the dimension table.
            //
            // Perk id rather than tree row: the row is not stored anywhere backend-side
            // (it would mean pulling the rune tree from DDragon into the ingestor), and
            // Riot assigns ids within a tree in row order anyway, so ordering by id
            // reproduces the tree order in practice. Any total order would fix the
            // duplication; this one is also the cheapest to reproduce in the SQL
            // backfill that merges the existing pairs.
            var secondary = perks
                .Where(perk => string.Equals(perk.StyleDescription, "subStyle", StringComparison.OrdinalIgnoreCase))
                .OrderBy(perk => perk.PerkId)
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
            && itemEvent.EventType.Equals(ItemEventTypes.Purchased, StringComparison.OrdinalIgnoreCase));
        var normalSkillLevelUps = row.SkillEvents.Count(skillEvent =>
            skillEvent.LevelUpType.Equals("NORMAL", StringComparison.OrdinalIgnoreCase));

        return purchaseCount > 0
            && row.GameDurationSeconds >= MinimumAggregatedGameDurationSeconds
            && normalSkillLevelUps >= 3
            && !string.IsNullOrWhiteSpace(row.Position);
    }
}
