using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

internal static class RiotMatchMapper
{
    public static MappedMatch Map(
        RiotMatchDto matchDto,
        string platformId,
        IReadOnlyDictionary<AccountKey, RiotAccount> participantAccounts)
    {
        var matchId = matchDto.Metadata.MatchId;
        var gameStartUtc = RiotValueConverters.ToUtcDateTime(matchDto.Info.GameStartTimestamp);

        var match = new Match
        {
            Id = matchId,
            PlatformId = platformId,
            QueueId = matchDto.Info.QueueId,
            MapId = matchDto.Info.MapId,
            GameMode = matchDto.Info.GameMode,
            GameType = matchDto.Info.GameType,
            GameStartTimeUtc = gameStartUtc ?? DateTime.UtcNow,
            GameDurationSeconds = RiotValueConverters.ToIntSafe(matchDto.Info.GameDuration),
            GameVersion = matchDto.Info.GameVersion,
            CreatedAtUtc = DateTime.UtcNow,
            TimelineIngested = false
        };

        var participants = MapParticipants(matchDto, matchId, platformId, participantAccounts);
        var perkSelectionRows = BuildPerkSelectionRows(matchDto, matchId);

        return new MappedMatch(match, participants, perkSelectionRows);
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
                TotalDamageDealtToChampions = participant.TotalDamageDealtToChampions,
                VisionScore = participant.VisionScore,
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

    internal static List<MappedPerkSelection> BuildPerkSelectionRows(RiotMatchDto match, string matchId)
    {
        var selections = new List<MappedPerkSelection>();
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

                    selections.Add(new MappedPerkSelection(
                        matchId,
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
}

internal sealed record MappedMatch(
    Match Match,
    IReadOnlyList<MatchParticipant> Participants,
    IReadOnlyList<MappedPerkSelection> PerkSelections);

internal sealed record MappedPerkSelection(string MatchId, int ParticipantId, PerkCatalogKey Key);
