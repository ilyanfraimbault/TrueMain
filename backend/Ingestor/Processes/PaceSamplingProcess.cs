using Core;
using Core.Lol.Identifiers;
using Core.Lol.Patches;
using Data.Aggregation;
using Data.Entities;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Components.PaceSampling;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Reads games of the tiers TrueMain does not ingest (Iron → Platinum by default) into the pace
/// benchmark (#1912), under its own hard request caps.
/// </summary>
/// <remarks>
/// <para>
/// Per platform and tier: one random page of the division ladder
/// (<c>league-v4/entries/RANKED_SOLO_5x5/{tier}/{division}?page=N</c>), a few random players
/// of it as seeds, each seed's latest ranked games, then each game's match and timeline. The
/// timeline is folded in memory into <c>pace_benchmark_stats</c> at the seed's tier — the
/// whole lobby, as the ingestion fold counts its lobbies at the tracked account's tier — and the
/// match is discarded.
/// </para>
/// <para>
/// <b>It never writes <c>matches</c> or <c>match_participants</c>.</b> Those rows would feed the
/// full-pool profile fold, the harvest would turn low-tier players into main candidates, and the
/// champion pages would silently gain low-elo games. A small ledger of sampled match ids
/// (<c>pace_sampled_matches</c>) is all it keeps, so a match is never counted twice; a match the
/// regular ingestion already stored is skipped for the same reason.
/// </para>
/// <para>
/// Every Riot call is charged — ladder page, id list, match, timeline, failed or not — against
/// <see cref="PaceSamplingOptions.MaxRequestsPerRun"/> and the day's
/// <see cref="PaceSamplingOptions.MaxRequestsPerDay"/>. Last in the fetch lane, so it spends only
/// what the ingestion left of the run's time.
/// </para>
/// </remarks>
public sealed class PaceSamplingProcess(
    ILogger<PaceSamplingProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IRiotMatchClient riotMatchClient,
    IDataSessionFactory sessionFactory,
    IProcessRunStore processRunStore,
    TimeProvider timeProvider,
    IOptions<PaceSamplingOptions> paceSamplingOptions) : IIngestorProcess
{
    private const string RankedSoloQueue = "RANKED_SOLO_5x5";
    private const int RankedSoloQueueId = 420;

    /// <summary>Calls one more game costs: its match and its timeline.</summary>
    private const int CallsPerMatch = 2;

    /// <summary>
    /// Failures in a row that end the run: one bad page is noise, this many is Riot or the key
    /// being down, and the budget is better kept for the next run.
    /// </summary>
    internal const int MaxConsecutiveFailures = 5;

    private static readonly string[] Divisions = ["I", "II", "III", "IV"];

    public string Name => "PaceSampling";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var options = paceSamplingOptions.Value;
        var platforms = PlatformNormalizer.Normalize(options.Platforms);
        var tiers = options.TierScope
            .Where(tier => !string.IsNullOrWhiteSpace(tier))
            .Select(tier => tier.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (options.MaxRequestsPerRun <= 0 || tiers.Count == 0 || platforms.Count == 0)
        {
            return new NoWorkSummary("Pace sampling disabled (no request budget, tier or platform configured).", 0);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (options.MinRunInterval > TimeSpan.Zero)
        {
            var lastRunUtc = await processRunStore.GetLastCompletedRunStartAsync(Name, ct);
            if (lastRunUtc is not null && nowUtc - lastRunUtc.Value < options.MinRunInterval)
            {
                return new SkippedSummary("Within MinRunInterval; pace sampling skipped this iteration.", true);
            }
        }

        var budget = await PaceSamplingBudget.ReadAsync(processRunStore, Name, options, nowUtc, ct);
        if (!budget.CanSpend(1 + 1 + CallsPerMatch))
        {
            logger.LogInformation(
                "Pace sampling skipped: daily budget spent ({SpentToday}/{MaxRequestsPerDay}).",
                budget.SpentToday,
                options.MaxRequestsPerDay);
            return new SkippedSummary("Daily request budget spent; pace sampling skipped this iteration.", true);
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        var run = new SamplingRun(options, budget, nowUtc);

        // A match older than the lookback can never be listed again, so its ledger row has
        // nothing left to guard.
        run.LedgerPruned = await session.PaceSampledMatches.DeleteSampledBeforeAsync(
            nowUtc - options.MatchLookback - TimeSpan.FromDays(1), ct);

        var slots = platforms
            .Where(platform => PlatformId.TryParse(platform, out _))
            .SelectMany(platform => tiers.Select(tier => (Platform: PlatformId.Parse(platform), Tier: tier)))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        // Round-robin over (platform, tier) so a budget that runs out mid-run still spreads
        // across every tier and region instead of filling the first one.
        var progressed = true;
        while (progressed && run.CanContinue && slots.Count > 0)
        {
            progressed = false;
            foreach (var (platform, tier) in slots)
            {
                if (!run.CanContinue || !budget.CanSpend(1 + 1 + CallsPerMatch))
                {
                    break;
                }

                await SampleSlotAsync(session, platform, tier, run, ct);
                progressed = true;
            }
        }

        var summary = run.ToSummary();
        logger.LogInformation(
            "Pace sampling summary: riotCalls={RiotCalls}, ladderPages={LadderPages}, sampled={Sampled}, known={Known}, unusable={Unusable}, failedCalls={Failed}, ledgerPruned={Pruned}.",
            summary.RiotCalls,
            summary.LadderPages,
            summary.MatchesSampled,
            summary.MatchesAlreadyKnown,
            summary.MatchesUnusable,
            summary.FailedCalls,
            summary.LedgerPruned);

        return summary;
    }

    private async Task SampleSlotAsync(
        IDataSession session,
        PlatformId platform,
        string tier,
        SamplingRun run,
        CancellationToken ct)
    {
        var division = Divisions[Random.Shared.Next(Divisions.Length)];
        var page = Random.Shared.Next(1, run.Options.MaxLadderPage + 1);

        var entries = await CallAsync(
            run,
            () => riotPlatformClient.GetLeagueEntriesAsync(platform.Route, RankedSoloQueue, tier, division, page, ct),
            $"{platform} {tier} {division} page {page}",
            ct);
        if (entries is null)
        {
            return;
        }

        run.LadderPages++;
        var seeds = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Puuid))
            .OrderBy(_ => Random.Shared.Next())
            .Take(run.Options.SeedsPerPage)
            .ToList();

        var region = platform.Route.ToRegional();
        foreach (var seed in seeds)
        {
            if (!run.CanContinue || !run.Budget.CanSpend(1 + CallsPerMatch))
            {
                return;
            }

            // The seed's own tier: a page can straddle a promotion, and the entry is the truth.
            var seedTier = string.IsNullOrWhiteSpace(seed.Tier) ? tier : seed.Tier!.Trim().ToUpperInvariant();
            var ids = await CallAsync(
                run,
                () => riotMatchClient.GetMatchIdsAsync(
                    new MatchIdQuery(seed.Puuid!, region, run.Options.MatchesPerSeed, RankedSoloQueueId, run.LookbackStartUtc),
                    ct),
                $"match ids of a {seedTier} seed on {platform}",
                ct);
            if (ids is null)
            {
                continue;
            }

            run.MatchIdLists++;
            foreach (var matchId in await NewMatchIdsAsync(session, ids, run, ct))
            {
                if (!run.CanContinue || !run.Budget.CanSpend(CallsPerMatch))
                {
                    return;
                }

                await SampleMatchAsync(session, matchId, region, seedTier, run, ct);
            }
        }
    }

    /// <summary>Drops ids already counted: by this run, by an earlier run, or by the regular ingestion.</summary>
    private static async Task<List<string>> NewMatchIdsAsync(
        IDataSession session,
        IReadOnlyCollection<string> ids,
        SamplingRun run,
        CancellationToken ct)
    {
        var candidates = ids.Distinct(StringComparer.Ordinal).ToList();
        var sampled = await session.PaceSampledMatches.GetSampledAsync(candidates, ct);
        var stored = await session.Matches.GetExistingMatchIdsAsync(candidates, ct);

        var fresh = new List<string>(candidates.Count);
        foreach (var id in candidates)
        {
            // Add returns false for a game another seed of this run already led to.
            if (!run.SeenMatchIds.Add(id) || sampled.Contains(id) || stored.Contains(id))
            {
                run.MatchesAlreadyKnown++;
                continue;
            }

            fresh.Add(id);
        }

        return fresh;
    }

    private async Task SampleMatchAsync(
        IDataSession session,
        string matchId,
        RegionalRoute region,
        string tier,
        SamplingRun run,
        CancellationToken ct)
    {
        var match = await CallAsync(run, () => riotMatchClient.GetMatchAsync(matchId, region, ct), $"match {matchId}", ct);
        if (match is null)
        {
            return;
        }

        var info = match.Info;
        var durationSeconds = RiotValueConverters.ToIntSafe(info.GameDuration);
        var endedInEarlySurrender = RiotMatchMapper.EndedInEarlySurrender(match);
        if (info.QueueId != RankedSoloQueueId
            || !RiotMatchMapper.IsCompletedGame(match)
            || ChampionCohort.IsRemake(durationSeconds, endedInEarlySurrender)
            || !PatchVersion.TryParse(info.GameVersion, out var version))
        {
            // Not worth its timeline call; remembered so no later run pays for the match again.
            run.MatchesUnusable++;
            await RecordAsync(session, matchId, tier, [], run, ct);
            return;
        }

        var timeline = await CallAsync(run, () => riotMatchClient.GetTimelineAsync(matchId, region, ct), $"timeline {matchId}", ct);
        if (timeline is null)
        {
            return;
        }

        var positions = info.Participants
            .Where(participant => ChampionCohort.IsCanonicalPosition(participant.TeamPosition))
            .GroupBy(participant => participant.ParticipantId)
            .ToDictionary(group => group.Key, group => group.First().TeamPosition);

        var keys = PaceBenchmarkBuilder.BuildAtTier(version.ToMajorMinor(), durationSeconds, endedInEarlySurrender, tier, positions, timeline);
        await RecordAsync(session, matchId, tier, keys, run, ct);

        run.MatchesSampled++;
        run.AddTier(tier, keys.Count / 2);
    }

    /// <summary>
    /// Writes a match's bins and its ledger row in one transaction: a crash between the two
    /// would either lose the match or count it again on the next run.
    /// </summary>
    private static async Task RecordAsync(
        IDataSession session,
        string matchId,
        string tier,
        IReadOnlyCollection<PaceBenchmarkKey> keys,
        SamplingRun run,
        CancellationToken ct)
    {
        await using var transaction = await session.BeginTransactionAsync(ct);
        await session.PaceBenchmarkStats.AddCountsAsync(PaceBenchmarkBuilder.Count(keys), run.NowUtc, ct);
        session.PaceSampledMatches.Add(new PaceSampledMatch { MatchId = matchId, Tier = tier, SampledAtUtc = run.NowUtc });
        await session.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        session.ClearTracking();
    }

    /// <summary>
    /// Charges one call and makes it. A failure is logged, counted and answered with
    /// <see langword="null"/>; enough of them in a row end the run.
    /// </summary>
    private async Task<T?> CallAsync<T>(SamplingRun run, Func<Task<T>> call, string what, CancellationToken ct)
        where T : class
    {
        run.Budget.Charge();
        try
        {
            var result = await call();
            run.ConsecutiveFailures = 0;
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            run.FailedCalls++;
            run.ConsecutiveFailures++;
            logger.LogWarning(ex, "Pace sampling: failed to read {What}.", what);
            return null;
        }
    }

    private sealed class SamplingRun(PaceSamplingOptions options, PaceSamplingBudget budget, DateTime nowUtc)
    {
        private readonly Dictionary<string, (int Matches, int LanerMinutes)> _tiers = new(StringComparer.Ordinal);

        public PaceSamplingOptions Options { get; } = options;
        public PaceSamplingBudget Budget { get; } = budget;
        public DateTime NowUtc { get; } = nowUtc;
        public DateTime LookbackStartUtc { get; } = nowUtc - options.MatchLookback;
        public HashSet<string> SeenMatchIds { get; } = new(StringComparer.Ordinal);

        public int LadderPages { get; set; }
        public int MatchIdLists { get; set; }
        public int MatchesSampled { get; set; }
        public int MatchesAlreadyKnown { get; set; }
        public int MatchesUnusable { get; set; }
        public int FailedCalls { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int LedgerPruned { get; set; }

        public bool CanContinue => ConsecutiveFailures < MaxConsecutiveFailures;

        public void AddTier(string tier, int lanerMinutes)
        {
            var (matches, minutes) = _tiers.GetValueOrDefault(tier);
            _tiers[tier] = (matches + 1, minutes + lanerMinutes);
        }

        public PaceSamplingSummary ToSummary() => new(
            Budget.Spent,
            LadderPages,
            MatchIdLists,
            MatchesSampled,
            MatchesAlreadyKnown,
            MatchesUnusable,
            FailedCalls,
            LedgerPruned,
            _tiers
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new PaceSamplingTierSummary(entry.Key, entry.Value.Matches, entry.Value.LanerMinutes))
                .ToList());
    }
}
