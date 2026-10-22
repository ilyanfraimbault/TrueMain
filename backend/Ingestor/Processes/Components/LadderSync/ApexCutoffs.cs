using Core.Lol.Ranking;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.LadderSync;

/// <summary>
/// The Grandmaster and Challenger thresholds the ranked card's LP curve draws (#2174): measured
/// from each apex league the ladder sync reads, and estimated once for the days before the first
/// measurement (#2181).
/// </summary>
public static class ApexCutoffs
{
    // As far back as the cutoffs endpoint serves: one scan of rank_snapshots either way.
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(730);

    // Below this many tracked snapshots on a day, the lowest of them says little about the
    // league's last player: the day is left without an estimate rather than guessed.
    private const int MinSnapshotsPerDay = 10;

    /// <summary>
    /// Stores the day's threshold of a league just read: its lowest LP. Master starts at a fixed
    /// 0 LP and an empty league has no threshold, so neither writes anything.
    /// </summary>
    public static async Task RecordAsync(
        IDataSessionFactory sessionFactory,
        string platformId,
        string tier,
        RiotLeagueListDto league,
        DateTime nowUtc,
        CancellationToken ct)
    {
        RankTier? cutoffTier = tier switch
        {
            "CHALLENGER" => RankTier.Challenger,
            "GRANDMASTER" => RankTier.Grandmaster,
            _ => null
        };
        if (cutoffTier is null || league.Entries.Count == 0)
        {
            return;
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        await session.ApexCutoffSnapshots.UpsertAsync(
            platformId,
            cutoffTier.Value,
            league.Entries.Min(entry => entry.LeaguePoints),
            league.Entries.Count,
            nowUtc,
            ct);
    }

    /// <summary>
    /// Estimates the thresholds of the days before the first measurement from the tracked
    /// accounts' snapshots, on the first apex refresh that finds no estimate yet; a no-op on every
    /// later one. Only when Grandmaster or Challenger is in the sweep's scope, the tiers it then
    /// measures. A failure is logged and left for the next refresh: the curve falls back to the
    /// measured days meanwhile, so it must never cost the ladder sync its run.
    /// </summary>
    public static async Task EstimateHistoryAsync(
        IDataSessionFactory sessionFactory,
        ILogger logger,
        IReadOnlyCollection<string> apexTiers,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!apexTiers.Contains("GRANDMASTER") && !apexTiers.Contains("CHALLENGER"))
        {
            return;
        }

        try
        {
            await using var session = await sessionFactory.CreateAsync(ct);
            var written = await session.ApexCutoffSnapshots.EstimateHistoryAsync(
                nowUtc - HistoryWindow, nowUtc, MinSnapshotsPerDay, ct);
            if (written > 0)
            {
                logger.LogInformation("Estimated {Rows} past apex cutoffs from rank snapshots.", written);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to estimate past apex cutoffs.");
        }
    }
}
