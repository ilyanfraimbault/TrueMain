using Core;
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

        var existingSet = await session.Matches.GetExistingMatchIdsAsync(allMatchIds, ct);
        var newMatchIds = allMatchIds
            .Where(id => !existingSet.Contains(id))
            .ToList();

        var inserted = 0;
        var skipped = allMatchIds.Count - newMatchIds.Count;
        var batchSize = Math.Max(1, saveBatchSize);

        for (var i = 0; i < newMatchIds.Count; i += batchSize)
        {
            var batch = newMatchIds.Skip(i).Take(batchSize).ToList();
            foreach (var matchId in batch)
            {
                var matchDto = await riotMatchClient.GetMatchAsync(matchId, region, ct);
                AddMatchSnapshot(session, matchDto, platformId);
                inserted++;
            }

            await session.SaveChangesAsync(ct);
        }

        return new SnapshotIngestionResult(allMatchIds, newMatchIds, inserted, skipped);
    }

    private static void AddMatchSnapshot(IDataSession session, RiotMatchDto matchDto, string platformId)
    {
        var matchId = matchDto.Metadata.MatchId;
        var gameStartUtc = RiotDataHelpers.ToUtcDateTime(matchDto.Info.GameStartTimestamp);

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

        session.MatchParticipants.AddRange(MapParticipants(matchDto, matchId));
        session.MatchParticipants.AddPerkSelections(MapPerkSelections(matchDto, matchId));
    }

    private static List<MatchParticipant> MapParticipants(RiotMatchDto match, string matchId)
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

    private static List<ParticipantPerkSelection> MapPerkSelections(RiotMatchDto match, string matchId)
    {
        var selections = new List<ParticipantPerkSelection>();
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

                    selections.Add(new ParticipantPerkSelection
                    {
                        MatchId = matchId,
                        ParticipantId = participant.ParticipantId,
                        StyleId = style.Style,
                        StyleDescription = style.Description ?? string.Empty,
                        SelectionIndex = index,
                        PerkId = selection.Perk
                    });
                }
            }
        }

        return selections;
    }
}
