using Core;
using Data;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingestor.Processes;

public class MatchIngestionProcess(
    ILogger<MatchIngestionProcess> logger,
    IRiotMatchClient riotMatchClient,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<MatchIngestionOptions> matchOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = matchOptions.Value;
        if (options.Platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (MatchIngestion:Platforms).");
            return;
        }

        var platforms = options.Platforms
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var claimedAccounts = await ClaimAccountsAsync(platforms, options.BatchSize, ct);
        if (claimedAccounts.Count == 0)
        {
            logger.LogInformation("No queued accounts to ingest.");
            return;
        }

        var summaryByPlatform = platforms.ToDictionary(p => p.ToUpperInvariant(), _ => new PlatformSummary());

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
                    continue;
                }

                var region = RiotRouting.FromPlatform(platform);
                var matchIds = (await riotMatchClient.GetMatchIdsAsync(account.Puuid, region, options.MatchesPerAccount, ct))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                await using var db = await dbContextFactory.CreateDbContextAsync(ct);

                var existingMatchIds = await db.Matches
                    .Where(m => matchIds.Contains(m.Id))
                    .Select(m => m.Id)
                    .ToListAsync(ct);

                var existingSet = existingMatchIds.ToHashSet(StringComparer.Ordinal);
                var newMatchIds = matchIds.Where(id => !existingSet.Contains(id)).ToList();

                var inserted = 0;
                var skipped = matchIds.Count - newMatchIds.Count;

                foreach (var matchId in newMatchIds)
                {
                    var matchDto = await riotMatchClient.GetMatchAsync(matchId, region, ct);
                    await UpsertMatchSnapshotAsync(db, matchDto, platformId, ct);
                    inserted++;
                }

                LogPendingChanges(logger, db, "MatchSnapshot", account.PlatformId, account.Puuid);
                await db.SaveChangesAsync(ct);

                var timelineUpdated = 0;
                foreach (var matchId in newMatchIds)
                {
                    var timelineDto = await riotMatchClient.GetTimelineAsync(matchId, region, ct);
                    await ApplyTimelineAsync(logger, db, matchId, timelineDto, ct);
                    timelineUpdated++;
                }

                await ValidateAccountAsync(account, ct);

                if (!summaryByPlatform.TryGetValue(platformId, out var summary))
                {
                    summary = new PlatformSummary();
                    summaryByPlatform[platformId] = summary;
                }

                summary.AccountsProcessed++;
                summary.MatchesInserted += inserted;
                summary.MatchesSkipped += skipped;
                summary.TimelinesUpdated += timelineUpdated;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Match ingestion failed for {Platform}/{Puuid}. Reverting to queued.", account.PlatformId, account.Puuid);
                await RevertToQueuedAsync(account, ct);
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
    }

    private async Task<List<AccountKey>> ClaimAccountsAsync(List<string> platforms, int batchSize, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var accounts = new List<AccountKey>();

        var connection = db.Database.GetDbConnection();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT DISTINCT ON ("PlatformId", "Puuid") "PlatformId", "Puuid"
                FROM main_candidates
                WHERE "Status" = @status
                  AND "PlatformId" = ANY(@platforms)
                ORDER BY "PlatformId", "Puuid", "Score" DESC
                LIMIT @batch;
                """;

            command.Transaction = (transaction as IDbContextTransaction)?.GetDbTransaction();

            command.Parameters.Add(new NpgsqlParameter("status", (int)MainCandidateStatus.Queued));
            command.Parameters.Add(new NpgsqlParameter("platforms", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = platforms.ToArray()
            });
            command.Parameters.Add(new NpgsqlParameter("batch", batchSize));

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                accounts.Add(new AccountKey(reader.GetString(0), reader.GetString(1)));
            }
        }

        var claimed = new List<AccountKey>();
        foreach (var account in accounts)
        {
            var updated = await db.MainCandidates
                .Where(c => c.Status == MainCandidateStatus.Queued &&
                            c.PlatformId == account.PlatformId &&
                            c.Puuid == account.Puuid)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, MainCandidateStatus.Processing), ct);

            if (updated > 0)
            {
                logger.LogDebug(
                    "Claimed {Count} candidates for {Platform}/{Puuid}.",
                    updated,
                    account.PlatformId,
                    account.Puuid);
                claimed.Add(account);
            }
        }

        await transaction.CommitAsync(ct);
        return claimed;
    }

    private async Task ValidateAccountAsync(AccountKey account, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var updated = await db.MainCandidates
            .Where(c => c.PlatformId == account.PlatformId && c.Puuid == account.Puuid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, MainCandidateStatus.Validated)
                .SetProperty(c => c.ValidatedAtUtc, nowUtc), ct);

        if (updated > 0)
        {
            logger.LogDebug(
                "Validated {Count} candidates for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }
    }

    private async Task RevertToQueuedAsync(AccountKey account, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var updated = await db.MainCandidates
            .Where(c => c.PlatformId == account.PlatformId && c.Puuid == account.Puuid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, MainCandidateStatus.Queued), ct);

        if (updated > 0)
        {
            logger.LogDebug(
                "Reverted {Count} candidates to Queued for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }
    }

    private static bool TryParsePlatform(string platform, out PlatformRoute route)
        => Enum.TryParse(platform.Trim(), ignoreCase: true, out route);

    private static async Task UpsertMatchSnapshotAsync(TrueMainDbContext db, RiotMatchDto matchDto, string platformId, CancellationToken ct)
    {
        var matchId = matchDto.Metadata.MatchId;

        var existingMatch = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (existingMatch is not null)
        {
            return;
        }

        var gameStartUtc = ToUtcDateTime(matchDto.Info.GameStartTimestamp);

        db.Matches.Add(new Match
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

        foreach (var participant in participants)
        {
            db.MatchParticipants.Add(participant);
        }

        db.ParticipantPerkSelections.AddRange(perkSelections);
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
        ILogger logger,
        TrueMainDbContext db,
        string matchId,
        MatchTimelineDto timeline,
        CancellationToken ct)
    {
        var participants = await db.MatchParticipants
            .Where(p => p.MatchId == matchId)
            .ToListAsync(ct);

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

        LogPendingChanges(logger, db, "MatchTimeline", null, null, matchId);
        await db.SaveChangesAsync(ct);
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

    private static void LogPendingChanges(
        ILogger logger,
        TrueMainDbContext db,
        string stage,
        string? platformId,
        string? puuid,
        string? matchId = null)
    {
        var added = 0;
        var modified = 0;
        var deleted = 0;

        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    added++;
                    break;
                case EntityState.Modified:
                    modified++;
                    break;
                case EntityState.Deleted:
                    deleted++;
                    break;
            }
        }

        if (added == 0 && modified == 0 && deleted == 0)
        {
            return;
        }

        logger.LogDebug(
            "{Stage} DB changes for {Platform}/{Puuid} match={MatchId}: added={Added}, modified={Modified}, deleted={Deleted}.",
            stage,
            platformId ?? "-",
            puuid ?? "-",
            matchId ?? "-",
            added,
            modified,
            deleted);
    }

    private sealed class PlatformSummary
    {
        public int AccountsProcessed { get; set; }
        public int MatchesInserted { get; set; }
        public int MatchesSkipped { get; set; }
        public int TimelinesUpdated { get; set; }
    }

    private sealed record AccountKey(string PlatformId, string Puuid);
}
