using Core.Lol.Map;
using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

public sealed class ChampionPatternSourceRowReader(
    IDbContextFactory<TrueMainDbContext> dbContextFactory)
{
    private const int MinimumAggregatedGameDurationSeconds = 15 * 60;

    internal async Task<ChampionPatternAggregationInputs> LoadAggregationInputsAsync(
        int queueId,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var existingAggregateScopes = await LoadExistingAggregateScopesAsync(db, queueId, ct);
        var sourceRows = await LoadSourceRowsAsync(db, queueId, ct);

        return new ChampionPatternAggregationInputs
        {
            ExistingAggregateScopes = existingAggregateScopes,
            SourceRows = sourceRows
        };
    }

    private static Task<List<AggregateScopeKey>> LoadExistingAggregateScopesAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        // Phase 6.4: source the cleanup keys from ChampionAggregateScope
        // since ChampionPatternAggregate is gone. Same semantic — replace
        // every scope that already exists for this queue.
        return db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId)
            .Select(scope => new AggregateScopeKey(
                scope.ChampionId,
                scope.GameVersion,
                scope.PlatformId,
                scope.QueueId))
            .Distinct()
            .ToListAsync(ct);
    }

    private static async Task<List<AggregateSourceRow>> LoadSourceRowsAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        var sourceRows = await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            join stat in db.MainChampionStats.AsNoTracking()
                on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                equals new { stat.PlatformId, stat.Puuid, stat.ChampionId }
            where stat.IsMain
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
        return filtered;
    }

    private static string NormalizeGameVersion(string gameVersion)
        => PatchVersion.TryParse(gameVersion, out var patch) ? patch.ToString() : gameVersion;

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
