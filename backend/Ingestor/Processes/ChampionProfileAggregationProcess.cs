using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Aggregation;
using Data.BuildFacts;
using Data.Entities;
using Data.Statics;
using Ingestor.Options;
using Ingestor.Processes.Components.ProfileAggregation;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Incrementally folds each match into <c>champion_profile_stats</c> (#1449): per
/// <c>(champion, position, patch)</c>, the additive sums of what the champion did —
/// damage split by type, healing and shielding, crowd control, damage taken and
/// mitigated, gold and XP leads over the lane opponent at 10 and 15 minutes, and the
/// item archetypes it completed — plus its ranged flag from Data Dragon.
///
/// Structurally <see cref="ChampionBanAggregationProcess"/> again: one fold per match
/// gated by <see cref="Match.ProfileAggregated"/>, additive rows via
/// <c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>, aged-out patches never revisited
/// (#466). Four things are specific to profiles:
///
/// 1. <b>The full pool, not the champion cohort.</b> A profile describes the champion,
///    not its mains, so every participant counts — harvested rows included. The
///    whole-match rules of <see cref="ChampionCohort"/> still apply: a remake is not a
///    game, and a participant without a canonical position has no lane.
///
/// 2. <b>Only participants carrying the #1448 context fields contribute.</b> The
///    matches ingested before those columns existed read <c>NULL</c> there; they are
///    flagged like any other match but add nothing, so the first pass over the retained
///    history is a scan, not a dilution. The lane and item families keep their own
///    denominators because their sources can be missing for a game the context fields
///    were measured on (a game that ended before 15 minutes, a patch whose item branch
///    is not published).
///
/// 3. <b>Item metadata is fatal, champion statics are not.</b> A metadata outage aborts
///    the run like it does for the powerspike fold — flagging a match without its
///    archetypes would lose them for good. The ranged flag is a static attribute stored
///    with <c>COALESCE</c>, so a Data Dragon outage just leaves it for the next batch.
///
/// 4. <b>Nothing is divided here.</b> Shares, means and per-minute rates are read-time
///    arithmetic over the sums, which keeps the fold additive and lets the read side
///    change a normalisation without re-folding anything.
/// </summary>
public sealed class ChampionProfileAggregationProcess(
    ILogger<ChampionProfileAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<ChampionProfileAggregationOptions> options,
    IItemMetadataProvider itemMetadataProvider,
    IChampionStaticsProvider championStaticsProvider,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private const int EarlyMinute = 10;
    private const int LaneMinute = 15;

    public string Name => "ChampionProfileAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var rangedThreshold = options.Value.RangedAttackRangeThreshold;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var batches = 0;
        var participants = 0;
        var rows = 0;

        // Per-run caches: one metadata and one statics lookup per patch, however many
        // batches touch it. Statics failures are remembered per patch too, so an outage
        // costs one warning per patch per run rather than one per batch.
        var itemMetadataByPatch = new Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>>(StringComparer.Ordinal);
        var rangedByPatch = new Dictionary<string, IReadOnlyDictionary<int, bool>?>(StringComparer.Ordinal);

        while (maxPerRun == 0 || processedMatches < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // TimelineIngested for the same reason the matchup fold gates on it: the lane
            // leads read the 10/15-minute snapshots, and flagging a match whose timeline
            // has not arrived would lose its lanes for good. IX_matches_profile_pending
            // keeps this an index scan once the initial backlog is drained.
            var matchIds = await db.Matches
                .AsNoTracking()
                .Where(m => m.QueueId == queueId && !m.ProfileAggregated && m.TimelineIngested)
                .OrderBy(m => m.Id)
                .Take(take)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (matchIds.Count == 0)
            {
                break;
            }

            var written = await ProcessBatchAsync(
                db, matchIds, itemMetadataByPatch, rangedByPatch, rangedThreshold, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            participants += written.Participants;
            rows += written.Rows;
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        logger.LogInformation(
            "Champion profile aggregation summary: matches={Matches}, batches={Batches}, "
            + "participants={Participants}, rows={Rows}.",
            processedMatches,
            batches,
            participants,
            rows);

        return new ChampionProfileAggregationSummary(processedMatches, batches, participants, rows);
    }

    private async Task<WrittenRows> ProcessBatchAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>> itemMetadataByPatch,
        Dictionary<string, IReadOnlyDictionary<int, bool>?> rangedByPatch,
        int rangedThreshold,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion, m.GameDurationSeconds })
            .ToDictionaryAsync(m => m.Id, ct);

        // Slim projection on purpose: never the ItemEvents/SkillEvents jsonb. The final
        // inventory (Item0..5) is what the archetypes read; the context fields are the
        // #1448 columns, nullable so an unmeasured row can be told from a zero.
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.Win,
                p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5,
                p.PhysicalDamageDealtToChampions,
                p.MagicDamageDealtToChampions,
                p.TrueDamageDealtToChampions,
                p.TotalHeal,
                p.TotalHealsOnTeammates,
                p.TotalDamageShieldedOnTeammates,
                p.TimeCCingOthers,
                p.TotalTimeCCDealt,
                p.TotalDamageTaken,
                p.DamageSelfMitigated))
            .ToListAsync(ct);

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Only the matches with at least one measured participant need their snapshots:
        // the pre-#1448 backlog folds to nothing and must not pay for a snapshot load.
        var measuredMatchIds = participantsByMatch
            .Where(kv => kv.Value.Any(p => p.HasContext))
            .Select(kv => kv.Key)
            .ToList();

        var readings = measuredMatchIds.Count == 0
            ? new Dictionary<(string MatchId, int ParticipantId, int Minute), Reading>()
            : await db.MatchParticipantTimelineSnapshots
                .AsNoTracking()
                .Where(s => measuredMatchIds.Contains(s.MatchId)
                    && (s.IntervalMinute == EarlyMinute || s.IntervalMinute == LaneMinute))
                .Select(s => new { s.MatchId, s.ParticipantId, s.IntervalMinute, s.TotalGold, s.Xp, s.Kills })
                .ToDictionaryAsync(
                    s => (s.MatchId, s.ParticipantId, s.IntervalMinute),
                    s => new Reading(s.TotalGold, s.Xp, s.Kills),
                    ct);

        var profiles = new Dictionary<ProfileKey, ProfileAccumulator>();
        var folded = 0;

        foreach (var matchId in measuredMatchIds)
        {
            var match = matches[matchId];
            var patch = PatchVersion.Normalize(match.GameVersion);
            if (string.IsNullOrEmpty(patch) || ChampionCohort.IsRemake(match.GameDurationSeconds))
            {
                continue;
            }

            var itemMetadata = await GetItemMetadataAsync(itemMetadataByPatch, patch, match.GameVersion, ct);
            var ranged = await GetRangedAsync(rangedByPatch, patch, match.GameVersion, rangedThreshold, ct);

            var parts = participantsByMatch[matchId];
            foreach (var self in parts)
            {
                if (!self.HasContext || !ChampionCohort.IsCanonicalPosition(self.TeamPosition))
                {
                    continue;
                }

                var key = new ProfileKey(self.ChampionId, self.TeamPosition, patch);
                if (!profiles.TryGetValue(key, out var acc))
                {
                    acc = new ProfileAccumulator();
                    profiles[key] = acc;
                }

                folded++;
                acc.Games++;
                if (self.Win)
                {
                    acc.Wins++;
                }

                acc.GameDurationSecondsSum += match.GameDurationSeconds;
                acc.PhysicalDamageSum += self.PhysicalDamage!.Value;
                acc.MagicDamageSum += self.MagicDamage!.Value;
                acc.TrueDamageSum += self.TrueDamage!.Value;
                acc.TotalHealSum += self.TotalHeal!.Value;
                acc.HealsOnTeammatesSum += self.HealsOnTeammates!.Value;
                acc.DamageShieldedSum += self.DamageShielded!.Value;
                acc.TimeCCingOthersSum += self.TimeCCingOthers!.Value;
                acc.TotalTimeCCDealtSum += self.TotalTimeCCDealt!.Value;
                acc.DamageTakenSum += self.DamageTaken!.Value;
                acc.DamageSelfMitigatedSum += self.DamageSelfMitigated!.Value;

                // The frontline share needs every teammate's damage taken. All ten rows of
                // a match are ingested together, so this only fails on a mixed match, but
                // the gate is kept explicit rather than assumed.
                var teammates = parts.Where(p => p.TeamId == self.TeamId).ToList();
                if (teammates.All(p => p.DamageTaken.HasValue))
                {
                    acc.TeamDamageTakenGames++;
                    acc.TeamDamageTakenSum += teammates.Sum(p => (long)p.DamageTaken!.Value);
                }

                var opponent = parts.FirstOrDefault(other =>
                    other.TeamPosition == self.TeamPosition && other.TeamId != self.TeamId);
                if (opponent is not null)
                {
                    if (readings.TryGetValue((matchId, self.ParticipantId, EarlyMinute), out var self10)
                        && readings.TryGetValue((matchId, opponent.ParticipantId, EarlyMinute), out var opp10))
                    {
                        acc.LaneGamesAt10++;
                        acc.GoldLeadAt10Sum += self10.TotalGold - opp10.TotalGold;
                        acc.XpLeadAt10Sum += self10.Xp - opp10.Xp;
                        acc.KillsBy10Sum += self10.Kills;
                    }

                    if (readings.TryGetValue((matchId, self.ParticipantId, LaneMinute), out var self15)
                        && readings.TryGetValue((matchId, opponent.ParticipantId, LaneMinute), out var opp15))
                    {
                        acc.LaneGamesAt15++;
                        acc.GoldLeadAt15Sum += self15.TotalGold - opp15.TotalGold;
                        acc.XpLeadAt15Sum += self15.Xp - opp15.Xp;
                    }
                }

                acc.ItemGames++;
                var archetypes = ItemArchetypes.ClassifyInventory(
                    [self.Item0, self.Item1, self.Item2, self.Item3, self.Item4, self.Item5], itemMetadata);
                acc.CritGames += archetypes.HasFlag(ItemArchetype.Crit) ? 1 : 0;
                acc.ArmorPenetrationGames += archetypes.HasFlag(ItemArchetype.ArmorPenetration) ? 1 : 0;
                acc.OnHitGames += archetypes.HasFlag(ItemArchetype.OnHit) ? 1 : 0;
                acc.AbilityPowerGames += archetypes.HasFlag(ItemArchetype.AbilityPower) ? 1 : 0;
                acc.TankGames += archetypes.HasFlag(ItemArchetype.Tank) ? 1 : 0;

                if (ranged is not null && ranged.TryGetValue(self.ChampionId, out var isRanged))
                {
                    acc.IsRanged = isRanged;
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ChampionProfileUpsert.WriteAsync(db, profiles, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProfileAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(folded, profiles.Count);
    }

    private async Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemMetadataAsync(
        Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>> cache,
        string patch,
        string gameVersion,
        CancellationToken ct)
    {
        if (!cache.TryGetValue(patch, out var metadata))
        {
            metadata = await itemMetadataProvider.GetItemsAsync(gameVersion, ct);
            cache[patch] = metadata;
        }

        return metadata;
    }

    private async Task<IReadOnlyDictionary<int, bool>?> GetRangedAsync(
        Dictionary<string, IReadOnlyDictionary<int, bool>?> cache,
        string patch,
        string gameVersion,
        int rangedThreshold,
        CancellationToken ct)
    {
        if (cache.TryGetValue(patch, out var ranged))
        {
            return ranged;
        }

        try
        {
            var statics = await championStaticsProvider.GetChampionsAsync(gameVersion, ct);
            ranged = statics.ToDictionary(kv => kv.Key, kv => kv.Value.AttackRange >= rangedThreshold);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A static attribute must never abort a fold: the flag is COALESCEd on write,
            // so the next batch or run that can reach Data Dragon fills it in.
            logger.LogWarning(exception,
                "Champion statics unavailable for patch {Patch}; the ranged flag is left for a later run.", patch);
            ranged = null;
        }

        cache[patch] = ranged;
        return ranged;
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        bool Win,
        int Item0, int Item1, int Item2, int Item3, int Item4, int Item5,
        int? PhysicalDamage,
        int? MagicDamage,
        int? TrueDamage,
        int? TotalHeal,
        int? HealsOnTeammates,
        int? DamageShielded,
        int? TimeCCingOthers,
        int? TotalTimeCCDealt,
        int? DamageTaken,
        int? DamageSelfMitigated)
    {
        /// <summary>
        /// Whether every #1448 field is present. Riot sends them together, so a row is
        /// measured or it is not; a partial row is treated as unmeasured rather than
        /// folding some sums and not others under one Games count.
        /// </summary>
        public bool HasContext =>
            PhysicalDamage.HasValue && MagicDamage.HasValue && TrueDamage.HasValue
            && TotalHeal.HasValue && HealsOnTeammates.HasValue && DamageShielded.HasValue
            && TimeCCingOthers.HasValue && TotalTimeCCDealt.HasValue
            && DamageTaken.HasValue && DamageSelfMitigated.HasValue;
    }

    private readonly record struct Reading(int TotalGold, int Xp, int Kills);

    private readonly record struct WrittenRows(int Participants, int Rows);
}
