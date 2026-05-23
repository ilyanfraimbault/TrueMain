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
        int page,
        int pageSize,
        string? position,
        int? championId,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        // Normalize the position filter once. The DB stores team positions
        // as upper-case Riot strings (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY);
        // any other value clamps to null so a bogus query param doesn't
        // wedge the comparison.
        var normalizedPosition = string.IsNullOrWhiteSpace(position)
            ? null
            : position.Trim().ToUpperInvariant();
        if (normalizedPosition is not null
            && normalizedPosition != "TOP"
            && normalizedPosition != "JUNGLE"
            && normalizedPosition != "MIDDLE"
            && normalizedPosition != "BOTTOM"
            && normalizedPosition != "UTILITY")
        {
            normalizedPosition = null;
        }

        var championFilter = championId is > 0 ? championId : null;

        // Multi-platform name-tag disambiguation: a (gameName, tagLine) pair
        // is unique within a Riot routing region but can collide across
        // regions. Picking the most-recently-active row keeps this endpoint
        // and `/truemains/{nameTag}/profile` (ProfileQueryService) aligned —
        // both routes always resolve to the same account for a given name
        // tag, so the user never lands on inconsistent profile vs. matches.
        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new { a.Id, a.Puuid })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var clampedPageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var clampedPage = page < 1 ? 1 : page;

        // Total first so the frontend can render the pagination control even
        // when it lands directly on a deep page via the URL. Filtered to the
        // same predicate as the data query — we never want the count and the
        // list to disagree about which matches "belong to" this player.
        //
        // No mode filter here: the ingestor pulls match ids with `type=ranked`
        // at the Riot API source (see RiotMatchClient.GetMatchIdsAsync), so
        // nothing non-ranked enters the DB going forward. Historical Arena
        // / ARAM rows that pre-date that change may still surface for some
        // accounts until they're either cleaned up or aged out.
        //
        // Position / champion filters live on the same `Any(...)` clause so
        // the count and the page slice share a single predicate. Both apply
        // to the self participant in the match — `p.Puuid == account.Puuid`
        // narrows to that row, and the optional extras filter on its
        // championId / teamPosition.
        var matchesQuery = db.Matches
            .AsNoTracking()
            .Where(m => m.Participants.Any(p =>
                p.Puuid == account.Puuid
                && (championFilter == null || p.ChampionId == championFilter)
                && (normalizedPosition == null || p.TeamPosition == normalizedPosition)));

        var total = await matchesQuery.CountAsync(ct);
        if (total == 0)
        {
            return new MatchSummariesResponse
            {
                Matches = Array.Empty<MatchSummaryReadModel>(),
                Page = 1,
                PageSize = clampedPageSize,
                Total = 0,
            };
        }

        var matchRows = await matchesQuery
            .OrderByDescending(m => m.GameStartTimeUtc)
            .Skip((clampedPage - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .Select(m => new MatchRow(
                m.Id,
                m.QueueId,
                m.GameMode,
                m.GameStartTimeUtc,
                m.GameDurationSeconds))
            .ToListAsync(ct);

        if (matchRows.Count == 0)
        {
            // Requested page is past the last one. Return an empty page with
            // the real total so the frontend's pagination control still
            // resolves to a valid range.
            return new MatchSummariesResponse
            {
                Matches = Array.Empty<MatchSummaryReadModel>(),
                Page = clampedPage,
                PageSize = clampedPageSize,
                Total = total,
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

        return new MatchSummariesResponse
        {
            Matches = matches,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = total,
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
