using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class DiscoveryProcess(
    ILogger<DiscoveryProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    ProcessRunRecorder runRecorder,
    IOptions<DiscoveryOptions> discoveryOptions)
{
    private const string RankedSoloQueue = "RANKED_SOLO_5x5";

    public async Task RunAsync(CancellationToken ct)
    {
        var options = discoveryOptions.Value;
        var startedAt = DateTime.UtcNow;
        var summaries = new List<PlatformSummary>();

        if (options.Platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (Discovery:Platforms).");
            return;
        }

        try
        {
            foreach (var platformString in options.Platforms.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();

                if (!TryParsePlatform(platformString, out var platform))
                {
                    logger.LogWarning("Skipping unknown platform '{Platform}'.", platformString);
                    continue;
                }

                var summary = new PlatformSummary(platformString.ToUpperInvariant());

                await using var session = await sessionFactory.CreateAsync(ct);

                var ladderEntries = await FetchLadderEntriesAsync(platform, options, ct);
                if (ladderEntries.Count == 0)
                {
                    logger.LogInformation("No ladder entries for platform {Platform}.", platform);
                    continue;
                }

                var boundedSummoners = ladderEntries
                    .DistinctBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(0, options.MaxAccountsPerPlatformPerRun))
                    .ToList();

                foreach (var entry in boundedSummoners)
                {
                    ct.ThrowIfCancellationRequested();

                    var summoner = await ResolveSummonerAsync(platform, entry, ct);
                    if (string.IsNullOrWhiteSpace(summoner.Puuid))
                    {
                        continue;
                    }

                    var puuid = summoner.Puuid;
                    var nowUtc = DateTime.UtcNow;

                    await UpsertRiotAccountAsync(session, platform, summoner, nowUtc, ct);

                    var masteries = await riotPlatformClient.GetChampionMasteriesAsync(platform, puuid, ct);
                    var topMasteries = masteries
                        .OrderByDescending(m => m.ChampionPoints)
                        .Take(Math.Max(0, options.TopChampionsPerAccount))
                        .ToList();

                    summary.AccountsProcessed++;

                    if (topMasteries.Count == 0)
                    {
                        continue;
                    }

                    var championIds = topMasteries.Select(m => m.ChampionId).ToList();
                    var existingCandidates = await session.MainCandidates
                        .GetByPlatformPuuidAndChampionsAsync(summary.PlatformId, puuid, championIds, ct);

                    var existingByChampion = existingCandidates.ToDictionary(c => c.ChampionId);

                    for (var i = 0; i < topMasteries.Count; i++)
                    {
                        var mastery = topMasteries[i];
                        var lastPlayUtc = ToUtcDateTime(mastery.LastPlayTime);
                        if (lastPlayUtc is null)
                        {
                            continue;
                        }

                        if (options.MaxLastPlayDays > 0 &&
                            nowUtc - lastPlayUtc.Value > TimeSpan.FromDays(options.MaxLastPlayDays))
                        {
                            continue;
                        }

                        var rank = i + 1;

                        if (existingByChampion.TryGetValue(mastery.ChampionId, out var candidate))
                        {
                            candidate.ChampionRankInMasteryTop = rank;
                            candidate.ChampionPoints = mastery.ChampionPoints;
                            candidate.LastPlayTimeUtc = lastPlayUtc.Value;
                            candidate.DiscoveredAtUtc = nowUtc;
                            summary.CandidatesUpdated++;
                        }
                        else
                        {
                            session.MainCandidates.Add(new MainCandidate
                            {
                                PlatformId = summary.PlatformId,
                                Puuid = puuid,
                                ChampionId = mastery.ChampionId,
                                ChampionRankInMasteryTop = rank,
                                ChampionPoints = mastery.ChampionPoints,
                                LastPlayTimeUtc = lastPlayUtc.Value,
                                DiscoveredAtUtc = nowUtc
                            });
                            summary.CandidatesInserted++;
                        }
                    }

                    await session.SaveChangesAsync(ct);
                }

                summaries.Add(summary);

                logger.LogInformation(
                    "Discovery summary for {Platform}: accounts={AccountsProcessed}, candidatesInserted={Inserted}, candidatesUpdated={Updated}.",
                    summary.PlatformId,
                    summary.AccountsProcessed,
                    summary.CandidatesInserted,
                    summary.CandidatesUpdated);
            }

            var finishedAt = DateTime.UtcNow;
            var summaryPayload = new
            {
                platforms = summaries.Select(s => new
                {
                    platform = s.PlatformId,
                    accountsProcessed = s.AccountsProcessed,
                    candidatesInserted = s.CandidatesInserted,
                    candidatesUpdated = s.CandidatesUpdated
                })
            };
            await runRecorder.RecordAsync("Discovery", startedAt, finishedAt, ProcessRunStatus.Success, summaryPayload, null, ct);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("Discovery", startedAt, finishedAt, ProcessRunStatus.Failed, null, ex.Message, ct);
            throw;
        }
    }

    private async Task<List<LadderEntry>> FetchLadderEntriesAsync(PlatformRoute platform, DiscoveryOptions options, CancellationToken ct)
    {
        var tierScope = options.TierScope.Select(t => t.Trim().ToUpperInvariant()).ToHashSet();
        var result = new List<LadderEntry>();

        if (tierScope.Contains("CHALLENGER"))
        {
            var challenger = await riotPlatformClient.GetChallengerLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(challenger.Entries
                .Select(ToLadderEntry)
                .Where(entry => entry is not null)!);
        }

        if (tierScope.Contains("GM") || tierScope.Contains("GRANDMASTER"))
        {
            var gm = await riotPlatformClient.GetGrandmasterLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(gm.Entries
                .Select(ToLadderEntry)
                .Where(entry => entry is not null)!);
        }

        if (tierScope.Contains("MASTER"))
        {
            var master = await riotPlatformClient.GetMasterLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(master.Entries
                .Select(ToLadderEntry)
                .Where(entry => entry is not null)!);
        }

        return result;
    }

    private static LadderEntry? ToLadderEntry(RiotLeagueEntryDto entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SummonerId))
        {
            return new LadderEntry(entry.SummonerId, entry.Puuid);
        }

        if (!string.IsNullOrWhiteSpace(entry.Puuid))
        {
            return new LadderEntry(null, entry.Puuid);
        }

        return null;
    }

    private async Task<RiotSummonerDto> ResolveSummonerAsync(PlatformRoute platform, LadderEntry entry, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entry.SummonerId))
        {
            return await riotPlatformClient.GetSummonerAsync(platform, entry.SummonerId, ct);
        }

        if (!string.IsNullOrWhiteSpace(entry.Puuid))
        {
            return await riotPlatformClient.GetSummonerByPuuidAsync(platform, entry.Puuid, ct);
        }

        return new RiotSummonerDto();
    }

    private static async Task UpsertRiotAccountAsync(
        IDataSession session,
        PlatformRoute platform,
        RiotSummonerDto summoner,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existing = await session.RiotAccounts.GetByPuuidAsync(summoner.Puuid, ct);
        var platformId = platform.ToString();

        if (existing is null)
        {
            session.RiotAccounts.Add(new RiotAccount
            {
                Puuid = summoner.Puuid,
                GameName = summoner.Name ?? string.Empty,
                TagLine = null,
                PlatformId = platformId,
                SummonerId = summoner.Id,
                ProfileIconId = summoner.ProfileIconId,
                SummonerLevel = ToIntSafe(summoner.SummonerLevel),
                UpdatedAtUtc = nowUtc,
                LastProfileSyncAtUtc = nowUtc
            });
            return;
        }

        existing.GameName = summoner.Name ?? string.Empty;
        existing.TagLine = null;
        existing.PlatformId = platformId;
        existing.SummonerId = summoner.Id;
        existing.ProfileIconId = summoner.ProfileIconId;
        existing.SummonerLevel = ToIntSafe(summoner.SummonerLevel);
        existing.UpdatedAtUtc = nowUtc;
        existing.LastProfileSyncAtUtc = nowUtc;
    }

    private static bool TryParsePlatform(string platform, out PlatformRoute route)
        => Enum.TryParse(platform.Trim(), ignoreCase: true, out route);

    private static DateTime? ToUtcDateTime(long lastPlayTimeMs)
    {
        if (lastPlayTimeMs <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(lastPlayTimeMs).UtcDateTime;
    }

    private static int ToIntSafe(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }


    private sealed class PlatformSummary(string platformId)
    {
        public string PlatformId { get; } = platformId;
        public int AccountsProcessed { get; set; }
        public int CandidatesInserted { get; set; }
        public int CandidatesUpdated { get; set; }
    }

    private sealed record LadderEntry(string? SummonerId, string? Puuid)
    {
        public string Key => SummonerId ?? Puuid ?? string.Empty;
    }
}
