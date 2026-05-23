using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public sealed class MatchSummariesQueryService(
    TrueMainDbContext db,
    ILogger<MatchSummariesQueryService> logger) : IMatchSummariesQueryService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    public async Task<MatchSummariesResponse?> GetAsync(
        string nameTag,
        int limit,
        DateTime? before,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .Select(a => new { a.Id, a.Puuid })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var pageSize = limit <= 0 ? DefaultPageSize : Math.Min(limit, MaxPageSize);

        // Page of matches the player participated in, newest first. Take one
        // extra row to know whether there's another page without a separate
        // count query.
        var matchesQuery = db.Matches
            .AsNoTracking()
            .Where(m => m.Participants.Any(p => p.Puuid == account.Puuid));

        if (before.HasValue)
        {
            matchesQuery = matchesQuery.Where(m => m.GameStartTimeUtc < before.Value);
        }

        var matchRows = await matchesQuery
            .OrderByDescending(m => m.GameStartTimeUtc)
            .Take(pageSize + 1)
            .Select(m => new MatchRow(
                m.Id,
                m.QueueId,
                m.GameMode,
                m.GameStartTimeUtc,
                m.GameDurationSeconds))
            .ToListAsync(ct);

        var hasMore = matchRows.Count > pageSize;
        if (hasMore)
        {
            matchRows = matchRows.Take(pageSize).ToList();
        }

        if (matchRows.Count == 0)
        {
            return new MatchSummariesResponse
            {
                Matches = Array.Empty<MatchSummaryReadModel>(),
                NextBefore = null,
            };
        }

        var matchIds = matchRows.Select(m => m.Id).ToList();

        // All participants across the page — needed for self stats, versus
        // thumbnails, team kills (KP%) and MVP/ACE derivation. One round trip.
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.Puuid,
                p.RiotAccountId,
                p.ChampionId,
                p.ChampLevel,
                p.TeamId,
                p.Win,
                p.Kills,
                p.Deaths,
                p.Assists,
                p.TotalMinionsKilled + p.NeutralMinionsKilled,
                p.Item0,
                p.Item1,
                p.Item2,
                p.Item3,
                p.Item4,
                p.Item5,
                p.TrinketItemId,
                p.PrimaryStyleId,
                p.SubStyleId,
                p.Summoner1Id,
                p.Summoner2Id))
            .ToListAsync(ct);

        // Riot account name+tag for the participants we can attribute. Only
        // the subset with a non-null RiotAccountId — others stay anonymous.
        var participantAccountIds = participants
            .Where(p => p.RiotAccountId.HasValue)
            .Select(p => p.RiotAccountId!.Value)
            .Distinct()
            .ToList();

        var accountsById = participantAccountIds.Count == 0
            ? new Dictionary<Guid, (string GameName, string? TagLine)>()
            : await db.RiotAccounts
                .AsNoTracking()
                .Where(a => participantAccountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.GameName, a.TagLine })
                .ToDictionaryAsync(a => a.Id, a => (a.GameName, a.TagLine), ct);

        // Keystone per (matchId, participantId, styleId): slot 0 of the
        // primary tree. We pull every slot-0 row for the page in one shot
        // and look up by the self participant's primary style.
        var keystoneRows = await (
            from pps in db.ParticipantPerkSelections.AsNoTracking()
            join cat in db.PerkSelectionCatalogs.AsNoTracking()
                on pps.PerkSelectionCatalogId equals cat.Id
            where matchIds.Contains(pps.MatchId) && cat.SelectionIndex == 0
            select new
            {
                pps.MatchId,
                pps.ParticipantId,
                cat.StyleId,
                cat.PerkId,
            }).ToListAsync(ct);

        var keystoneByKey = keystoneRows
            .GroupBy(k => (k.MatchId, k.ParticipantId, k.StyleId))
            .ToDictionary(g => g.Key, g => g.First().PerkId);

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matches = new List<MatchSummaryReadModel>(matchRows.Count);
        foreach (var match in matchRows)
        {
            if (!participantsByMatch.TryGetValue(match.Id, out var partList))
            {
                logger.LogWarning(
                    "[match-summaries] match has no participant rows match_id={MatchId}",
                    match.Id);
                continue;
            }

            var self = partList.FirstOrDefault(p => p.Puuid == account.Puuid);
            if (self is null)
            {
                logger.LogWarning(
                    "[match-summaries] match missing self participant match_id={MatchId} puuid={Puuid}",
                    match.Id, account.Puuid);
                continue;
            }

            var teamKills = partList
                .Where(p => p.TeamId == self.TeamId)
                .Sum(p => p.Kills);
            var killParticipation = teamKills == 0
                ? 0d
                : (double)(self.Kills + self.Assists) / teamKills;

            // KDA proxy for MVP/ACE — better-than-nothing until the dedicated
            // score metric lands. Tiebreak on raw (kills + assists) so a
            // perfect-no-deaths bot game doesn't tie ten ways.
            var bestPerSide = partList
                .GroupBy(p => p.Win)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(p => KdaScore(p.Kills, p.Deaths, p.Assists))
                        .ThenByDescending(p => p.Kills + p.Assists)
                        .First());

            var isMvp = self.Win
                && bestPerSide.TryGetValue(true, out var bestWin)
                && bestWin.ParticipantId == self.ParticipantId;
            var isAce = !self.Win
                && bestPerSide.TryGetValue(false, out var bestLose)
                && bestLose.ParticipantId == self.ParticipantId;

            keystoneByKey.TryGetValue((self.MatchId, self.ParticipantId, self.PrimaryStyleId), out var keystoneId);

            var participantList = partList
                .OrderBy(p => p.TeamId)
                .ThenBy(p => p.ParticipantId)
                .Select(p =>
                {
                    string? gameName = null;
                    string? tagLine = null;
                    if (p.RiotAccountId.HasValue
                        && accountsById.TryGetValue(p.RiotAccountId.Value, out var acc))
                    {
                        gameName = acc.GameName;
                        tagLine = acc.TagLine;
                    }
                    return new MatchSummaryParticipantReadModel
                    {
                        ChampionId = p.ChampionId,
                        TeamId = p.TeamId,
                        GameName = gameName,
                        TagLine = tagLine,
                    };
                })
                .ToList();

            matches.Add(new MatchSummaryReadModel
            {
                MatchId = match.Id,
                QueueId = match.QueueId,
                GameMode = match.GameMode,
                GameStartTimeUtc = match.GameStartTimeUtc,
                GameDurationSeconds = match.GameDurationSeconds,
                Self = new MatchSummarySelfReadModel
                {
                    ChampionId = self.ChampionId,
                    ChampionLevel = self.ChampLevel,
                    Summoner1Id = self.Summoner1Id,
                    Summoner2Id = self.Summoner2Id,
                    PrimaryStyleId = self.PrimaryStyleId,
                    SubStyleId = self.SubStyleId,
                    KeystoneId = keystoneId,
                    Kills = self.Kills,
                    Deaths = self.Deaths,
                    Assists = self.Assists,
                    Cs = self.Cs,
                    KillParticipation = killParticipation,
                    Items = new[]
                    {
                        self.Item0, self.Item1, self.Item2,
                        self.Item3, self.Item4, self.Item5,
                    },
                    TrinketItemId = self.TrinketItemId,
                    TeamId = self.TeamId,
                    Win = self.Win,
                    LpDelta = null,
                    IsMvp = isMvp,
                    IsAce = isAce,
                },
                Participants = participantList,
            });
        }

        var nextBefore = hasMore && matches.Count > 0
            ? matches[^1].GameStartTimeUtc
            : (DateTime?)null;

        return new MatchSummariesResponse
        {
            Matches = matches,
            NextBefore = nextBefore,
        };
    }

    private static double KdaScore(int kills, int deaths, int assists)
        => (kills + assists) / Math.Max(1d, deaths);

    private sealed record MatchRow(
        string Id,
        int QueueId,
        string GameMode,
        DateTime GameStartTimeUtc,
        int GameDurationSeconds);

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        string Puuid,
        Guid? RiotAccountId,
        int ChampionId,
        int ChampLevel,
        int TeamId,
        bool Win,
        int Kills,
        int Deaths,
        int Assists,
        int Cs,
        int Item0,
        int Item1,
        int Item2,
        int Item3,
        int Item4,
        int Item5,
        int TrinketItemId,
        int PrimaryStyleId,
        int SubStyleId,
        int Summoner1Id,
        int Summoner2Id);
}
