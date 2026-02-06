using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class MatchIngestionProcess(
    ILogger<MatchIngestionProcess> logger,
    IRiotMatchClient riotMatchClient,
    IDataSessionFactory sessionFactory,
    ProcessRunRecorder runRecorder,
    IOptions<MatchIngestionOptions> matchOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var options = matchOptions.Value;
        if (options.Platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (MatchIngestion:Platforms).");
            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync(
                "MatchIngestion",
                startedAt,
                finishedAt,
                ProcessRunStatus.Success,
                new { reason = "No platforms configured." },
                null,
                ct);
            return;
        }

        var platforms = options.Platforms
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var claimedAccounts = await ClaimAccountsAsync(platforms, options.BatchSize, ct);
        var summaryByPlatform = platforms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.ToUpperInvariant(), _ => new PlatformSummary());

        var totalAccounts = 0;
        var totalInserted = 0;
        var totalSkipped = 0;
        var totalTimelines = 0;
        var totalErrors = 0;

        try
        {
            if (claimedAccounts.Count == 0)
            {
                logger.LogInformation("No queued accounts to ingest.");
            }
            else
            {
                foreach (var account in claimedAccounts)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var platformId = account.PlatformId.ToUpperInvariant();
                        if (!TryParsePlatform(platformId, out var platform))
                        {
                            logger.LogWarning("Unknown platform {Platform}. Reverting queued status.", platformId);
                            await RevertToQueuedAsync(account, ct);
                            totalErrors++;
                            continue;
                        }

                        var region = RiotRouting.FromPlatform(platform);
                        var matchIds = (await riotMatchClient.GetMatchIdsAsync(account.Puuid, region, options.MatchesPerAccount, ct))
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                        await using var session = await sessionFactory.CreateAsync(ct);
                        var existingSet = await session.Matches.GetExistingMatchIdsAsync(matchIds, ct);
                        var newMatchIds = matchIds.Where(id => !existingSet.Contains(id)).ToList();

                        var inserted = 0;
                        var skipped = matchIds.Count - newMatchIds.Count;

                        foreach (var matchId in newMatchIds)
                        {
                            var matchDto = await riotMatchClient.GetMatchAsync(matchId, region, ct);
                            await UpsertMatchSnapshotAsync(session, matchDto, platformId, ct);
                            inserted++;
                        }

                        await session.SaveChangesAsync(ct);

                        var timelineUpdated = 0;
                        foreach (var matchId in newMatchIds)
                        {
                            var timelineDto = await riotMatchClient.GetTimelineAsync(matchId, region, ct);
                            await ApplyTimelineAsync(session, matchId, timelineDto, ct);
                            timelineUpdated++;
                        }

                        await session.SaveChangesAsync(ct);
                        await ValidateAccountAsync(account, ct);

                        logger.LogInformation(
                            "Match ingestion for {Platform}/{Puuid}: inserted={Inserted}, skipped={Skipped}, timelinesUpdated={Timelines}.",
                            platformId,
                            account.Puuid,
                            inserted,
                            skipped,
                            timelineUpdated);

                        if (!summaryByPlatform.TryGetValue(platformId, out var summary))
                        {
                            summary = new PlatformSummary();
                            summaryByPlatform[platformId] = summary;
                        }

                        summary.AccountsProcessed++;
                        summary.MatchesInserted += inserted;
                        summary.MatchesSkipped += skipped;
                        summary.TimelinesUpdated += timelineUpdated;

                        totalAccounts++;
                        totalInserted += inserted;
                        totalSkipped += skipped;
                        totalTimelines += timelineUpdated;
                    }
                    catch (Exception ex)
                    {
                        totalErrors++;
                        logger.LogError(ex, "Match ingestion failed for {Platform}/{Puuid}. Reverting to queued.", account.PlatformId, account.Puuid);
                        await RevertToQueuedAsync(account, ct);
                    }
                }
            }

            foreach (var (platformId, summary) in summaryByPlatform)
            {
                if (summary.AccountsProcessed == 0)
                {
                    continue;
                }

                logger.LogInformation(
                    "Match ingestion summary for {Platform}: accounts={Accounts}, matchesInserted={Inserted}, matchesSkipped={Skipped}, timelinesUpdated={Timelines}.",
                    platformId,
                    summary.AccountsProcessed,
                    summary.MatchesInserted,
                    summary.MatchesSkipped,
                    summary.TimelinesUpdated);
            }

            var finishedAt = DateTime.UtcNow;
            var summaryPayload = new
            {
                accountsProcessed = totalAccounts,
                matchesInserted = totalInserted,
                matchesSkipped = totalSkipped,
                timelinesUpdated = totalTimelines,
                errors = totalErrors,
                byPlatform = summaryByPlatform
                    .Where(kvp => kvp.Value.AccountsProcessed > 0)
                    .Select(kvp => new
                    {
                        platform = kvp.Key,
                        accountsProcessed = kvp.Value.AccountsProcessed,
                        matchesInserted = kvp.Value.MatchesInserted,
                        matchesSkipped = kvp.Value.MatchesSkipped,
                        timelinesUpdated = kvp.Value.TimelinesUpdated
                    })
                    .ToList()
            };

            await runRecorder.RecordAsync(
                "MatchIngestion",
                startedAt,
                finishedAt,
                ProcessRunStatus.Success,
                summaryPayload,
                null,
                ct);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync(
                "MatchIngestion",
                startedAt,
                finishedAt,
                ProcessRunStatus.Failed,
                null,
                ex.Message,
                ct);
            throw;
        }
    }

    private async Task<List<AccountKey>> ClaimAccountsAsync(List<string> platforms, int batchSize, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        await using var transaction = await session.BeginTransactionAsync(ct);

        var accounts = await session.RiotAccounts
            .ClaimAccountsForMatchIngestAsync(platforms, batchSize, ct);

        var claimed = new List<AccountKey>();
        foreach (var account in accounts)
        {
            var updated = await session.MainCandidates
                .SetStatusForAccountAsync(account.PlatformId, account.Puuid, MainCandidateStatus.Queued, MainCandidateStatus.Processing, ct);

            if (updated > 0)
            {
                logger.LogDebug(
                    "Claimed {Count} candidates for {Platform}/{Puuid}.",
                    updated,
                    account.PlatformId,
                    account.Puuid);
            }

            claimed.Add(account);
        }

        await session.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return claimed;
    }

    private async Task ValidateAccountAsync(AccountKey account, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var updated = await session.MainCandidates
            .SetStatusForAccountAsync(account.PlatformId, account.Puuid, MainCandidateStatus.Processing, MainCandidateStatus.Validated, ct);

        if (updated > 0)
        {
            logger.LogDebug(
                "Validated {Count} candidates for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }

        await session.RiotAccounts.UpdateLastMatchIngestAtAsync(account.PlatformId, account.Puuid, nowUtc, ct);
        await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
        await session.SaveChangesAsync(ct);
    }

    private async Task RevertToQueuedAsync(AccountKey account, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var updated = await session.MainCandidates
            .SetStatusForAccountAsync(account.PlatformId, account.Puuid, MainCandidateStatus.Processing, MainCandidateStatus.Queued, ct);

        if (updated > 0)
        {
            logger.LogDebug(
                "Reverted {Count} candidates to Queued for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }

        await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
        await session.SaveChangesAsync(ct);
    }

    private static bool TryParsePlatform(string platform, out PlatformRoute route)
        => Enum.TryParse(platform.Trim(), ignoreCase: true, out route);

    private static Task UpsertMatchSnapshotAsync(IDataSession session, RiotMatchDto matchDto, string platformId, CancellationToken ct)
    {
        var matchId = matchDto.Metadata.MatchId;

        var gameStartUtc = ToUtcDateTime(matchDto.Info.GameStartTimestamp);

        session.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = platformId,
            QueueId = matchDto.Info.QueueId,
            MapId = matchDto.Info.MapId,
            GameMode = matchDto.Info.GameMode ?? string.Empty,
            GameType = matchDto.Info.GameType ?? string.Empty,
            GameStartTimeUtc = gameStartUtc ?? DateTime.UtcNow,
            GameDurationSeconds = ToIntSafe(matchDto.Info.GameDuration),
            GameVersion = matchDto.Info.GameVersion ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        });

        var participants = MapParticipants(matchDto, matchId);
        var perkSelections = MapPerkSelections(matchDto, matchId);

        session.MatchParticipants.AddRange(participants);
        session.MatchParticipants.AddPerkSelections(perkSelections);

        return Task.CompletedTask;
    }

    private static List<MatchParticipant> MapParticipants(RiotMatchDto match, string matchId)
    {
        var participants = new List<MatchParticipant>(match.Info.Participants.Count);

        foreach (var p in match.Info.Participants)
        {
            var primaryStyle = p.Perks.Styles.FirstOrDefault(s =>
                string.Equals(s.Description, "primaryStyle", StringComparison.OrdinalIgnoreCase));
            var subStyle = p.Perks.Styles.FirstOrDefault(s =>
                string.Equals(s.Description, "subStyle", StringComparison.OrdinalIgnoreCase));

            participants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = p.ParticipantId,
                Puuid = p.Puuid,
                SummonerName = p.SummonerName,
                SummonerLevel = p.SummonerLevel,
                ChampionId = p.ChampionId,
                TeamId = p.TeamId,
                TeamPosition = p.TeamPosition ?? string.Empty,
                IndividualPosition = p.IndividualPosition ?? string.Empty,
                Lane = p.Lane ?? string.Empty,
                Role = p.Role ?? string.Empty,
                Win = p.Win,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                GoldEarned = p.GoldEarned,
                TotalMinionsKilled = p.TotalMinionsKilled,
                NeutralMinionsKilled = p.NeutralMinionsKilled,
                ChampLevel = p.ChampLevel,
                Item0 = p.Item0,
                Item1 = p.Item1,
                Item2 = p.Item2,
                Item3 = p.Item3,
                Item4 = p.Item4,
                Item5 = p.Item5,
                Item6 = p.Item6,
                TrinketItemId = p.Item6,
                PerksDefense = p.Perks.StatPerks.Defense,
                PerksFlex = p.Perks.StatPerks.Flex,
                PerksOffense = p.Perks.StatPerks.Offense,
                PrimaryStyleId = primaryStyle?.Style ?? 0,
                SubStyleId = subStyle?.Style ?? 0,
                Summoner1Id = p.Summoner1Id,
                Summoner2Id = p.Summoner2Id,
                ItemEvents = new List<ItemEvent>(),
                SkillEvents = new List<SkillEvent>()
            });
        }

        return participants;
    }

    private static List<ParticipantPerkSelection> MapPerkSelections(RiotMatchDto match, string matchId)
    {
        var selections = new List<ParticipantPerkSelection>();
        var seen = new HashSet<(int ParticipantId, int StyleId, int SelectionIndex)>();

        foreach (var participant in match.Info.Participants)
        {
            foreach (var style in participant.Perks.Styles)
            {
                for (var i = 0; i < style.Selections.Count; i++)
                {
                    var selection = style.Selections[i];
                    var key = (participant.ParticipantId, style.Style, i);
                    if (!seen.Add(key))
                    {
                        continue;
                    }
                    selections.Add(new ParticipantPerkSelection
                    {
                        MatchId = matchId,
                        ParticipantId = participant.ParticipantId,
                        StyleId = style.Style,
                        StyleDescription = style.Description ?? string.Empty,
                        SelectionIndex = i,
                        PerkId = selection.Perk
                    });
                }
            }
        }

        return selections;
    }

    private static async Task ApplyTimelineAsync(
        IDataSession session,
        string matchId,
        MatchTimelineDto timeline,
        CancellationToken ct)
    {
        var participants = await session.MatchParticipants.GetByMatchIdAsync(matchId, ct);

        if (participants.Count == 0)
        {
            return;
        }

        var itemEventsByParticipant = new Dictionary<int, List<ItemEvent>>();
        var skillEventsByParticipant = new Dictionary<int, List<SkillEvent>>();

        foreach (var evt in timeline.Events)
        {
            var participantId = evt.ParticipantId;

            if (participantId <= 0)
            {
                continue;
            }

            if (evt.Type.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase) && evt.ItemId.HasValue)
            {
                if (!itemEventsByParticipant.TryGetValue(participantId, out var itemEvents))
                {
                    itemEvents = new List<ItemEvent>();
                    itemEventsByParticipant[participantId] = itemEvents;
                }

                itemEvents.Add(new ItemEvent
                {
                    TimestampMs = evt.TimestampMs,
                    EventType = evt.Type,
                    ItemId = evt.ItemId.Value,
                    BeforeId = evt.BeforeId,
                    AfterId = evt.AfterId
                });
            }

            if (string.Equals(evt.Type, "SKILL_LEVEL_UP", StringComparison.OrdinalIgnoreCase) && evt.SkillSlot.HasValue)
            {
                if (!skillEventsByParticipant.TryGetValue(participantId, out var skillEvents))
                {
                    skillEvents = new List<SkillEvent>();
                    skillEventsByParticipant[participantId] = skillEvents;
                }

                skillEvents.Add(new SkillEvent
                {
                    TimestampMs = evt.TimestampMs,
                    SkillSlot = evt.SkillSlot.Value,
                    LevelUpType = evt.LevelUpType ?? string.Empty
                });
            }
        }

        foreach (var participant in participants)
        {
            participant.ItemEvents = itemEventsByParticipant.TryGetValue(participant.ParticipantId, out var itemEvents)
                ? itemEvents
                : new List<ItemEvent>();

            participant.SkillEvents = skillEventsByParticipant.TryGetValue(participant.ParticipantId, out var skillEvents)
                ? skillEvents
                : new List<SkillEvent>();
        }

        await session.SaveChangesAsync(ct);
    }

    private static DateTime? ToUtcDateTime(long timestampMs)
    {
        if (timestampMs <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;
    }

    private static int ToIntSafe(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private sealed class PlatformSummary
    {
        public int AccountsProcessed { get; set; }
        public int MatchesInserted { get; set; }
        public int MatchesSkipped { get; set; }
        public int TimelinesUpdated { get; set; }
    }

}
