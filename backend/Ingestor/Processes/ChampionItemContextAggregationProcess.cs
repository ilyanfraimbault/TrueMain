using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Aggregation;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes.Components.ItemContextAggregation;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Folds each match into the situational item context (#1450): for every item a champion
/// completed, how often it was built in games sitting at one end of a draft axis, against
/// how many games that end held. Then, at the end of the run, rebuilds the verdicts the
/// page reads from those counters.
///
/// <para>
/// <b>Two halves, on purpose.</b> The counters are additive and folded once per match
/// (<see cref="Match.ItemContextAggregated"/>), like every sibling aggregate, so frozen
/// patches freeze with the matches they were folded from (#466). The verdicts are
/// <em>derived</em> and rebuilt wholesale for the scopes a run touched — which is what makes
/// the API read a lookup with no statistics in it, and means a re-tuned threshold costs a
/// verdict rebuild rather than a re-fold of the retained history.
/// </para>
///
/// <para>
/// <b>The draft is qualified from measured profiles, not from labels.</b> Every axis comes
/// from the <c>champion_profile_stats</c> of the nine other participants (#1449), resolved
/// once per patch through <see cref="ChampionProfileSnapshot"/> — which deliberately reaches
/// back a patch or two, because profiles fill over a patch and a draft qualified against an
/// empty profile is not qualified at all.
/// </para>
///
/// <para>
/// <b>The cohort is the champion page's.</b> The champion side is <see cref="ChampionCohort"/>
/// — a main of that champion, tracked, canonical position, not a remake — the same
/// population the build panels this annotates are folded from. Anything wider would put a
/// sentence about one set of games under a tree drawn from another. The other nine
/// participants are whoever was in the game, main or not, exactly as in the matchup and
/// synergy folds.
/// </para>
///
/// <para>
/// <b>No elo dimension.</b> Unlike the matchup and ban aggregates, this one is not split by
/// rank. A situation is far rarer than a champion, and cutting the games eleven ways starves
/// the buckets the whole feature rests on; the verdicts therefore describe every rank
/// together, and the read says so rather than letting a card look rank-scoped.
/// </para>
/// </summary>
public sealed class ChampionItemContextAggregationProcess(
    ILogger<ChampionItemContextAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<ItemContextAggregationOptions> options,
    IItemMetadataProvider itemMetadataProvider,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private const int LaneLeadMinute = 15;

    public string Name => "ChampionItemContextAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var queueId = (int)analysisOptions.Value.QueueId;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var batches = 0;
        var participants = 0;
        var touched = new HashSet<ItemContextScope>();

        // Per-run caches: one metadata load and one profile snapshot per patch, however
        // many batches touch it.
        var itemMetadataByPatch = new Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>>(StringComparer.Ordinal);
        var profilesByPatch = new Dictionary<string, ChampionProfileSnapshot>(StringComparer.Ordinal);

        while (settings.MaxMatchesPerRun == 0 || processedMatches < settings.MaxMatchesPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = settings.MaxMatchesPerRun == 0
                ? settings.MatchBatchSize
                : Math.Min(settings.MatchBatchSize, settings.MaxMatchesPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // TimelineIngested for the same reason as the sibling folds: the gold-lead axis
            // reads the 15-minute snapshot, and flagging a match whose timeline has not
            // arrived would lose that axis for it permanently.
            var matchIds = await db.Matches
                .AsNoTracking()
                .Where(m => m.QueueId == queueId && !m.ItemContextAggregated && m.TimelineIngested)
                .OrderBy(m => m.Id)
                .Take(take)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (matchIds.Count == 0)
            {
                break;
            }

            var folded = await ProcessBatchAsync(
                db, matchIds, itemMetadataByPatch, profilesByPatch, settings, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            participants += folded.Participants;
            touched.UnionWith(folded.Scopes);
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        var verdicts = await RebuildVerdictsAsync(touched, settings, aggregatedAtUtc, ct);

        logger.LogInformation(
            "Champion item context aggregation summary: matches={Matches}, batches={Batches}, "
            + "participants={Participants}, scopes={Scopes}, verdicts={Verdicts}.",
            processedMatches,
            batches,
            participants,
            touched.Count,
            verdicts);

        return new ChampionItemContextAggregationSummary(
            processedMatches, batches, participants, touched.Count, verdicts);
    }

    private async Task<(int Participants, IReadOnlyCollection<ItemContextScope> Scopes)> ProcessBatchAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>> itemMetadataByPatch,
        Dictionary<string, ChampionProfileSnapshot> profilesByPatch,
        ItemContextAggregationOptions settings,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion, m.Patch, m.GameDurationSeconds })
            .ToDictionaryAsync(m => m.Id, ct);

        var cohort = await ChampionCohort.LoadAsync(db, matchIds, ct);

        // Every participant, slim: the nine others are only ever read for their champion,
        // position and side, which is what the axes are computed from.
        var slim = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new SlimParticipant(
                p.MatchId, p.ParticipantId, p.ChampionId, p.TeamId, p.TeamPosition, p.Win))
            .ToListAsync(ct);

        var cohortMatchIds = slim
            .Where(p => cohort.Includes(p.MatchId, p.ParticipantId))
            .Select(p => p.MatchId)
            .ToHashSet(StringComparer.Ordinal);

        if (cohortMatchIds.Count == 0)
        {
            await FlagAsync(db, matchIds, ct);
            return (0, []);
        }

        // The item timelines, the one heavy column here, are loaded only for the tracked
        // participants of the matches that actually have a cohort member — a couple of rows
        // per match instead of ten, which is what keeps this fold's working set flat.
        var builds = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => cohortMatchIds.Contains(p.MatchId) && p.RiotAccountId != null)
            .Select(p => new BuildRow(
                p.MatchId,
                p.ParticipantId,
                new[] { p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5, p.Item6 },
                p.ItemEvents))
            .ToDictionaryAsync(row => (row.MatchId, row.ParticipantId), ct);

        var leads = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => cohortMatchIds.Contains(s.MatchId) && s.IntervalMinute == LaneLeadMinute)
            .Select(s => new { s.MatchId, s.ParticipantId, s.TotalGold })
            .ToDictionaryAsync(s => (s.MatchId, s.ParticipantId), s => s.TotalGold, ct);

        var byMatch = slim.GroupBy(p => p.MatchId).ToDictionary(g => g.Key, g => g.ToList());
        var accumulator = new ItemContextAccumulator();
        var folded = 0;

        foreach (var matchId in cohortMatchIds)
        {
            var match = matches[matchId];
            var patch = match.Patch ?? PatchVersion.Normalize(match.GameVersion);
            if (string.IsNullOrEmpty(patch) || !cohort.IncludesMatch(matchId))
            {
                continue;
            }

            var metadata = await GetItemMetadataAsync(itemMetadataByPatch, patch, match.GameVersion, ct);
            var profiles = await GetProfilesAsync(profilesByPatch, db, patch, settings, ct);
            var roster = byMatch[matchId];

            foreach (var self in roster)
            {
                if (!cohort.Includes(matchId, self.ParticipantId)
                    || !builds.TryGetValue((matchId, self.ParticipantId), out var build))
                {
                    continue;
                }

                var opponent = roster.FirstOrDefault(other =>
                    other.TeamPosition == self.TeamPosition && other.TeamId != self.TeamId);

                var axes = DraftAxisEvaluator.Evaluate(
                    new DraftContext(
                        Side(roster.Where(other => other.TeamId != self.TeamId), profiles),
                        Side(roster.Where(other => other.TeamId == self.TeamId && other.ParticipantId != self.ParticipantId), profiles),
                        opponent is null ? null : profiles.Find(opponent.ChampionId, opponent.TeamPosition),
                        GoldLead(leads, matchId, self, opponent)),
                    settings.Axes);

                var scope = new ItemContextScope(self.ChampionId, self.TeamPosition, patch);
                foreach (var (slot, items) in ItemContextSlotResolver.Resolve(build.ItemEvents, build.FinalItems, metadata))
                {
                    accumulator.Add(scope, slot, items, axes, self.Win);
                }

                folded++;
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ItemContextUpsert.WriteAsync(db, accumulator, aggregatedAtUtc, ct);
        await FlagAsync(db, matchIds, ct);
        await transaction.CommitAsync(ct);

        return (folded, accumulator.Scopes);
    }

    /// <summary>
    /// Rebuilds the verdicts of every scope this run touched, one champion at a time so the
    /// working set stays bounded by a single champion's counters (the #600 lesson). The
    /// window handed to the builder is the served patch plus the older patches that actually
    /// carry rows for that champion — a gap in the patch history must not be counted as a
    /// patch's worth of games.
    /// </summary>
    private async Task<int> RebuildVerdictsAsync(
        IReadOnlyCollection<ItemContextScope> scopes,
        ItemContextAggregationOptions settings,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (scopes.Count == 0)
        {
            return 0;
        }

        var written = 0;

        foreach (var championScopes in scopes.GroupBy(scope => scope.ChampionId))
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var championId = championScopes.Key;

            var stats = await db.ChampionItemContextStats
                .AsNoTracking()
                .Where(row => row.ChampionId == championId)
                .ToListAsync(ct);
            var totals = await db.ChampionItemContextTotals
                .AsNoTracking()
                .Where(row => row.ChampionId == championId)
                .ToListAsync(ct);

            foreach (var scope in championScopes)
            {
                var scopeStats = stats
                    .Where(row => row.Position == scope.Position)
                    .ToList();
                var scopeTotals = totals
                    .Where(row => row.Position == scope.Position)
                    .ToList();

                var window = PatchWindow(scopeStats, scope.Patch, settings.MaxPatchLookback);
                var verdicts = ItemContextVerdictBuilder.Build(
                    scope, scopeStats, scopeTotals, window, settings, aggregatedAtUtc);

                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                await db.ChampionItemContextVerdicts
                    .Where(row => row.ChampionId == scope.ChampionId
                        && row.Position == scope.Position
                        && row.Patch == scope.Patch)
                    .ExecuteDeleteAsync(ct);

                if (verdicts.Count > 0)
                {
                    db.ChampionItemContextVerdicts.AddRange(verdicts);
                    await db.SaveChangesAsync(ct);
                }

                await transaction.CommitAsync(ct);
                written += verdicts.Count;
            }
        }

        return written;
    }

    private static IReadOnlyList<string> PatchWindow(
        IReadOnlyList<ChampionItemContextStat> stats,
        string servedPatch,
        int lookback)
    {
        if (!PatchVersion.TryParse(servedPatch, out var target))
        {
            return [];
        }

        return
        [
            .. stats
                .Select(row => row.Patch)
                .Distinct(StringComparer.Ordinal)
                .Select(raw => PatchVersion.TryParse(raw, out var version) ? (Raw: raw, Version: version) : default)
                .Where(entry => entry.Raw is not null && entry.Version <= target)
                .OrderByDescending(entry => entry.Version)
                .Take(lookback + 1)
                .Select(entry => entry.Raw)
        ];
    }

    private static DraftSide Side(IEnumerable<SlimParticipant> members, ChampionProfileSnapshot profiles)
    {
        var facts = new List<ChampionProfileFacts>();
        var missing = 0;
        foreach (var member in members)
        {
            var resolved = profiles.Find(member.ChampionId, member.TeamPosition);
            if (resolved is null)
            {
                missing++;
            }
            else
            {
                facts.Add(resolved);
            }
        }

        return new DraftSide(facts, missing);
    }

    private static double? GoldLead(
        IReadOnlyDictionary<(string, int), int> leads,
        string matchId,
        SlimParticipant self,
        SlimParticipant? opponent)
        => opponent is not null
            && leads.TryGetValue((matchId, self.ParticipantId), out var mine)
            && leads.TryGetValue((matchId, opponent.ParticipantId), out var theirs)
                ? mine - theirs
                : null;

    private static Task FlagAsync(TrueMainDbContext db, List<string> matchIds, CancellationToken ct)
        => db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ItemContextAggregated, true), ct);

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

    private static async Task<ChampionProfileSnapshot> GetProfilesAsync(
        Dictionary<string, ChampionProfileSnapshot> cache,
        TrueMainDbContext db,
        string patch,
        ItemContextAggregationOptions settings,
        CancellationToken ct)
    {
        if (!cache.TryGetValue(patch, out var snapshot))
        {
            snapshot = await ChampionProfileSnapshot.LoadAsync(
                db, patch, settings.ProfileLookbackPatches, settings.MinProfileGames, ct);
            cache[patch] = snapshot;
        }

        return snapshot;
    }

    private sealed record SlimParticipant(
        string MatchId, int ParticipantId, int ChampionId, int TeamId, string TeamPosition, bool Win);

    private sealed record BuildRow(
        string MatchId, int ParticipantId, int[] FinalItems, List<ItemEvent> ItemEvents);
}
