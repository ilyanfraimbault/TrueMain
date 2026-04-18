using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class MatchSnapshotWriter(IRiotMatchClient riotMatchClient) : IMatchSnapshotWriter
{
    public async Task<SnapshotIngestionResult> IngestSnapshotsAsync(
        IDataSession session,
        string platformId,
        string puuid,
        RegionalRoute region,
        int matchesPerAccount,
        int saveBatchSize,
        CancellationToken ct)
    {
        var allMatchIds = (await riotMatchClient.GetMatchIdsAsync(puuid, region, matchesPerAccount, ct))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var trackedAccount = await session.RiotAccounts.GetByKeyAsync(platformId, puuid, ct);
        var existingSet = await session.Matches.GetExistingMatchIdsAsync(allMatchIds, ct);
        var existingMatchIds = allMatchIds
            .Where(id => existingSet.Contains(id))
            .ToList();
        var newMatchIds = allMatchIds
            .Where(id => !existingSet.Contains(id))
            .ToList();

        var inserted = 0;
        var skipped = allMatchIds.Count - newMatchIds.Count;
        var batchSize = Math.Max(1, saveBatchSize);

        if (trackedAccount is not null && existingMatchIds.Count > 0)
        {
            await BackfillTrackedParticipantAccountIdsAsync(
                session,
                existingMatchIds,
                puuid,
                trackedAccount.Id,
                batchSize,
                ct);
        }

        for (var i = 0; i < newMatchIds.Count; i += batchSize)
        {
            var batch = newMatchIds.Skip(i).Take(batchSize).ToList();
            foreach (var matchId in batch)
            {
                var matchDto = await riotMatchClient.GetMatchAsync(matchId, region, ct);
                await AddMatchSnapshotAsync(session, matchDto, platformId, ct);
                inserted++;
            }

            await session.SaveChangesAsync(ct);
        }

        return new SnapshotIngestionResult(allMatchIds, newMatchIds, inserted, skipped);
    }

    private static async Task AddMatchSnapshotAsync(
        IDataSession session,
        RiotMatchDto matchDto,
        string platformId,
        CancellationToken ct)
    {
        var matchId = matchDto.Metadata.MatchId;
        var gameStartUtc = RiotDataHelpers.ToUtcDateTime(matchDto.Info.GameStartTimestamp);
        var participantAccounts = await session.RiotAccounts.GetByKeysAsync(
            matchDto.Info.Participants
                .Select(participant => new AccountKey(platformId, participant.Puuid))
                .Distinct()
                .ToArray(),
            ct);

        session.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = platformId,
            QueueId = matchDto.Info.QueueId,
            MapId = matchDto.Info.MapId,
            GameMode = matchDto.Info.GameMode,
            GameType = matchDto.Info.GameType,
            GameStartTimeUtc = gameStartUtc ?? DateTime.UtcNow,
            GameDurationSeconds = RiotDataHelpers.ToIntSafe(matchDto.Info.GameDuration),
            GameVersion = matchDto.Info.GameVersion,
            CreatedAtUtc = DateTime.UtcNow,
            TimelineIngested = false
        });

        session.MatchParticipants.AddRange(MapParticipants(matchDto, matchId, platformId, participantAccounts));

        var mappedSelections = BuildPerkSelectionRows(matchDto)
            .Select(selection => new MappedPerkSelection(matchId, selection.ParticipantId, selection.Key))
            .ToList();
        var catalogIdsByKey = await session.MatchParticipants.GetOrCreatePerkCatalogIdsAsync(
            mappedSelections.Select(selection => selection.Key).ToArray(),
            ct);

        var perkSelections = mappedSelections.Select(selection => new ParticipantPerkSelection
        {
            MatchId = selection.MatchId,
            ParticipantId = selection.ParticipantId,
            PerkSelectionCatalogId = catalogIdsByKey[selection.Key]
        });

        session.MatchParticipants.AddPerkSelections(perkSelections);
    }

    private static async Task BackfillTrackedParticipantAccountIdsAsync(
        IDataSession session,
        IReadOnlyCollection<string> existingMatchIds,
        string trackedPuuid,
        Guid trackedRiotAccountId,
        int batchSize,
        CancellationToken ct)
    {
        if (existingMatchIds.Count == 0)
        {
            return;
        }

        var participantsToUpdate = (await session.MatchParticipants.GetByMatchIdsAsync(existingMatchIds, ct))
            .Where(participant =>
                participant.RiotAccountId == null &&
                string.Equals(participant.Puuid, trackedPuuid, StringComparison.Ordinal))
            .ToList();

        var pendingUpdates = 0;

        foreach (var trackedParticipant in participantsToUpdate)
        {
            trackedParticipant.RiotAccountId = trackedRiotAccountId;
            pendingUpdates++;

            if (pendingUpdates < batchSize)
            {
                continue;
            }

            await session.SaveChangesAsync(ct);
            pendingUpdates = 0;
        }

        if (pendingUpdates > 0)
        {
            await session.SaveChangesAsync(ct);
        }
    }

    private static List<MatchParticipant> MapParticipants(
        RiotMatchDto match,
        string matchId,
        string platformId,
        IReadOnlyDictionary<AccountKey, RiotAccount> participantAccounts)
    {
        var participants = new List<MatchParticipant>(match.Info.Participants.Count);

        foreach (var participant in match.Info.Participants)
        {
            var primaryStyle = participant.Perks.Styles.FirstOrDefault(style =>
                string.Equals(style.Description, "primaryStyle", StringComparison.OrdinalIgnoreCase));
            var subStyle = participant.Perks.Styles.FirstOrDefault(style =>
                string.Equals(style.Description, "subStyle", StringComparison.OrdinalIgnoreCase));

            participants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = participant.ParticipantId,
                Puuid = participant.Puuid,
                RiotAccountId = participantAccounts.TryGetValue(new AccountKey(platformId, participant.Puuid), out var riotAccount)
                    ? riotAccount.Id
                    : null,
                SummonerName = participant.SummonerName,
                SummonerLevel = participant.SummonerLevel,
                ChampionId = participant.ChampionId,
                TeamId = participant.TeamId,
                TeamPosition = participant.TeamPosition,
                IndividualPosition = participant.IndividualPosition,
                Lane = participant.Lane,
                Role = participant.Role,
                Win = participant.Win,
                Kills = participant.Kills,
                Deaths = participant.Deaths,
                Assists = participant.Assists,
                GoldEarned = participant.GoldEarned,
                TotalMinionsKilled = participant.TotalMinionsKilled,
                NeutralMinionsKilled = participant.NeutralMinionsKilled,
                ChampLevel = participant.ChampLevel,
                Item0 = participant.Item0,
                Item1 = participant.Item1,
                Item2 = participant.Item2,
                Item3 = participant.Item3,
                Item4 = participant.Item4,
                Item5 = participant.Item5,
                Item6 = participant.Item6,
                TrinketItemId = participant.Item6,
                PerksDefense = participant.Perks.StatPerks.Defense,
                PerksFlex = participant.Perks.StatPerks.Flex,
                PerksOffense = participant.Perks.StatPerks.Offense,
                PrimaryStyleId = primaryStyle?.Style ?? 0,
                SubStyleId = subStyle?.Style ?? 0,
                Summoner1Id = participant.Summoner1Id,
                Summoner2Id = participant.Summoner2Id,
                ItemEvents = [],
                SkillEvents = []
            });
        }

        return participants;
    }

    internal static List<(int ParticipantId, PerkCatalogKey Key)> BuildPerkSelectionRows(RiotMatchDto match)
    {
        var selections = new List<(int ParticipantId, PerkCatalogKey Key)>();
        var seen = new HashSet<(int ParticipantId, int StyleId, int SelectionIndex)>();

        foreach (var participant in match.Info.Participants)
        {
            foreach (var style in participant.Perks.Styles)
            {
                for (var index = 0; index < style.Selections.Count; index++)
                {
                    var selection = style.Selections[index];
                    var key = (participant.ParticipantId, style.Style, index);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    selections.Add((
                        participant.ParticipantId,
                        new PerkCatalogKey(
                            style.Style,
                            index,
                            selection.Perk,
                            style.Description ?? string.Empty)));
                }
            }
        }

        return selections;
    }

    private sealed record MappedPerkSelection(string MatchId, int ParticipantId, PerkCatalogKey Key);
}
