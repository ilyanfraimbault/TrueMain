using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Read path for the single-match detail page
/// (<c>GET /truemains/{nameTag}/matches/{matchId}</c>). Loads the match header,
/// all 10 participants with their build order / skill order / rune page, the
/// @15 timeline snapshots and a temporally-nearest rank snapshot per tracked
/// account — then computes the derived per-minute rates and laning diffs
/// server-side so the frontend renders them directly.
///
/// Scope per issue #523: no team objectives, no performance/MVP/ACE score, no
/// ward counts — only data the DB already has.
/// </summary>
public sealed class MatchDetailQueryService(TrueMainDbContext db) : IMatchDetailQueryService
{
    private const int LaningIntervalMinute = 15;

    public async Task<MatchDetailReadModel?> GetAsync(string nameTag, string matchId, CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed) || string.IsNullOrWhiteSpace(matchId))
        {
            return null;
        }

        // Resolve the route's account the same way the rest of the truemain
        // routes do (most-recently-active row on a name-tag collision). The
        // account only scopes the URL — the payload covers every participant —
        // but it must exist and must have actually played this match, so a
        // stray match id under someone else's slug 404s instead of leaking a
        // game the player wasn't in.
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

        var match = await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => new
            {
                m.Id,
                m.QueueId,
                m.GameMode,
                m.GameStartTimeUtc,
                m.GameDurationSeconds,
                m.GameVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            return null;
        }

        // Full participant entities — the ItemEvents / SkillEvents JSON columns
        // are owned collections, so we materialize the rows rather than project
        // each scalar by hand.
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.MatchId == matchId)
            .ToListAsync(ct);

        if (participants.Count == 0)
        {
            return null;
        }

        // The route account must be one of this match's participants. Guards
        // against /truemains/{someone}/matches/{a-match-they-never-played}.
        if (participants.All(p => p.Puuid != account.Puuid))
        {
            return null;
        }

        var participantIds = participants.Select(p => p.ParticipantId).ToList();

        // Full 6-rune pages for every participant: ParticipantPerkSelection
        // joined to its catalog row. Final ordering (keystone-first,
        // primary-tree-then-secondary-tree) is applied per participant below.
        var perkRows = await (
            from pps in db.ParticipantPerkSelections.AsNoTracking()
            join cat in db.PerkSelectionCatalogs.AsNoTracking()
                on pps.PerkSelectionCatalogId equals cat.Id
            where pps.MatchId == matchId
            select new PerkRow(
                pps.ParticipantId,
                cat.StyleId,
                cat.SelectionIndex,
                cat.PerkId)).ToListAsync(ct);

        // Group raw rune rows per participant; the final keystone-first,
        // primary-tree-then-secondary-tree ordering is applied per participant
        // below, where the participant's PrimaryStyleId is known (SelectionIndex
        // resets to 0 within each tree, so it alone can't order across trees).
        var perkRowsByParticipant = perkRows
            .GroupBy(r => r.ParticipantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // @15 timeline snapshots — one per participant when present. Used for
        // the laning-phase cs/gold/xp diffs against the lane opponent.
        var snapshots = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => s.MatchId == matchId && s.IntervalMinute == LaningIntervalMinute)
            .Select(s => new Snapshot15(
                s.ParticipantId,
                s.MinionsKilled + s.JungleMinionsKilled,
                s.TotalGold,
                s.Xp))
            .ToListAsync(ct);

        var snapshotByParticipant = snapshots
            .GroupBy(s => s.ParticipantId)
            .ToDictionary(g => g.Key, g => g.First());

        // Nearest rank snapshot per tracked account, by absolute distance from
        // the game's start time. One LINQ pass: for each participant's account
        // pick the snapshot whose CapturedAtUtc is closest to GameStartTimeUtc.
        var trackedAccountIds = participants
            .Where(p => p.RiotAccountId.HasValue)
            .Select(p => p.RiotAccountId!.Value)
            .Distinct()
            .ToList();

        var rankByAccount = new Dictionary<Guid, MatchDetailRankReadModel>();
        if (trackedAccountIds.Count > 0)
        {
            var gameStart = match.GameStartTimeUtc;

            // Group the account's snapshots and pick the temporally-closest one.
            // EF can't translate the abs-diff ordering, so we pull the candidate
            // rows (a single account's rank history is small) and reduce in
            // memory.
            var rankRows = await db.RankSnapshots
                .AsNoTracking()
                .Where(s => trackedAccountIds.Contains(s.RiotAccountId))
                .Select(s => new
                {
                    s.RiotAccountId,
                    s.CapturedAtUtc,
                    s.Tier,
                    s.Division,
                    s.LeaguePoints,
                })
                .ToListAsync(ct);

            rankByAccount = rankRows
                .GroupBy(s => s.RiotAccountId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var nearest = g
                            .OrderBy(s => Math.Abs((s.CapturedAtUtc - gameStart).Ticks))
                            .ThenByDescending(s => s.CapturedAtUtc)
                            .First();
                        return new MatchDetailRankReadModel
                        {
                            Tier = nearest.Tier,
                            Division = nearest.Division,
                            LeaguePoints = nearest.LeaguePoints,
                        };
                    });
        }

        // Riot ids for the tracked participants, so the scoreboard can show
        // a name#tag and deep-link to other profiles.
        var accountsById = trackedAccountIds.Count == 0
            ? new Dictionary<Guid, (string GameName, string? TagLine)>()
            : await db.RiotAccounts
                .AsNoTracking()
                .Where(a => trackedAccountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.GameName, a.TagLine })
                .ToDictionaryAsync(a => a.Id, a => (a.GameName, a.TagLine), ct);

        // Team kills per side for KP%.
        var teamKills = participants
            .GroupBy(p => p.TeamId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Kills));

        var durationMinutes = match.GameDurationSeconds > 0
            ? match.GameDurationSeconds / 60d
            : 0d;

        // Lane opponent = the participant on the other team sharing the same
        // non-empty TeamPosition. Built once; used for @15 diffs and first-to-2.
        var opponentByParticipant = BuildOpponentMap(participants);

        var participantModels = participants
            .OrderBy(p => p.TeamId)
            .ThenBy(p => p.ParticipantId)
            .Select(p =>
            {
                var cs = p.TotalMinionsKilled + p.NeutralMinionsKilled;

                var sideKills = teamKills.TryGetValue(p.TeamId, out var tk) ? tk : 0;
                var kp = sideKills == 0
                    ? 0d
                    : (double)(p.Kills + p.Assists) / sideKills;

                string? gameName = null;
                string? tagLine = null;
                MatchDetailRankReadModel? rank = null;
                if (p.RiotAccountId.HasValue)
                {
                    if (accountsById.TryGetValue(p.RiotAccountId.Value, out var acc))
                    {
                        gameName = acc.GameName;
                        tagLine = acc.TagLine;
                    }
                    rankByAccount.TryGetValue(p.RiotAccountId.Value, out rank);
                }

                opponentByParticipant.TryGetValue(p.ParticipantId, out var opponent);

                var laning15 = ComputeLaning15(p.ParticipantId, opponent, snapshotByParticipant);
                var firstToTwo = ComputeFirstToLevelTwo(p, opponent);

                var primaryStyleId = p.PrimaryStyleId;
                var runes = (perkRowsByParticipant.TryGetValue(p.ParticipantId, out var rs)
                        ? rs
                        : new List<PerkRow>())
                    // Primary tree first (keystone-first within it), then the
                    // secondary tree — SelectionIndex resets per tree, so the
                    // primary-style flag must lead the sort.
                    .OrderBy(r => r.StyleId == primaryStyleId ? 0 : 1)
                    .ThenBy(r => r.SelectionIndex)
                    .ThenBy(r => r.PerkId)
                    .Select(r => new MatchDetailRuneReadModel
                    {
                        StyleId = r.StyleId,
                        SelectionIndex = r.SelectionIndex,
                        PerkId = r.PerkId,
                    })
                    .ToList();

                var keystoneId = runes
                    .Where(r => r.StyleId == p.PrimaryStyleId && r.SelectionIndex == 0)
                    .Select(r => r.PerkId)
                    .DefaultIfEmpty(0)
                    .First();

                var itemEvents = p.ItemEvents
                    .OrderBy(e => e.TimestampMs)
                    .Select(e => new MatchDetailItemEventReadModel
                    {
                        TimestampMs = e.TimestampMs,
                        EventType = e.EventType,
                        ItemId = e.ItemId,
                        BeforeId = e.BeforeId,
                        AfterId = e.AfterId,
                    })
                    .ToList();

                var skillEvents = p.SkillEvents
                    .OrderBy(e => e.TimestampMs)
                    .Select(e => new MatchDetailSkillEventReadModel
                    {
                        TimestampMs = e.TimestampMs,
                        SkillSlot = e.SkillSlot,
                    })
                    .ToList();

                return new MatchDetailParticipantReadModel
                {
                    ParticipantId = p.ParticipantId,
                    ChampionId = p.ChampionId,
                    ChampLevel = p.ChampLevel,
                    SummonerName = p.SummonerName,
                    GameName = gameName,
                    TagLine = tagLine,
                    TeamId = p.TeamId,
                    TeamPosition = p.TeamPosition,
                    Win = p.Win,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Assists = p.Assists,
                    Items = new[]
                    {
                        p.Item0, p.Item1, p.Item2,
                        p.Item3, p.Item4, p.Item5, p.Item6,
                    },
                    TrinketItemId = p.TrinketItemId,
                    Summoner1Id = p.Summoner1Id,
                    Summoner2Id = p.Summoner2Id,
                    PrimaryStyleId = p.PrimaryStyleId,
                    SubStyleId = p.SubStyleId,
                    KeystoneId = keystoneId,
                    TotalDamageDealtToChampions = p.TotalDamageDealtToChampions,
                    VisionScore = p.VisionScore,
                    GoldEarned = p.GoldEarned,
                    Cs = cs,
                    Rank = rank,
                    KillParticipation = kp,
                    CsPerMin = PerMin(cs, durationMinutes),
                    DamagePerMin = PerMin(p.TotalDamageDealtToChampions, durationMinutes),
                    GoldPerMin = PerMin(p.GoldEarned, durationMinutes),
                    VisionPerMin = PerMin(p.VisionScore, durationMinutes),
                    Laning15 = laning15,
                    FirstToLevelTwo = firstToTwo,
                    Runes = runes,
                    StatPerkOffense = p.PerksOffense,
                    StatPerkFlex = p.PerksFlex,
                    StatPerkDefense = p.PerksDefense,
                    ItemEvents = itemEvents,
                    SkillEvents = skillEvents,
                };
            })
            .ToList();

        return new MatchDetailReadModel
        {
            MatchId = match.Id,
            QueueId = match.QueueId,
            GameMode = match.GameMode,
            GameStartTimeUtc = match.GameStartTimeUtc,
            GameDurationSeconds = match.GameDurationSeconds,
            GameVersion = match.GameVersion,
            Participants = participantModels,
        };
    }

    private static double PerMin(int value, double minutes)
        => minutes <= 0 ? 0d : value / minutes;

    /// <summary>
    /// Maps each participant id to its lane opponent — the single participant on
    /// the other team sharing the same non-empty <c>TeamPosition</c>. Positions
    /// with anything other than exactly one player per side (e.g. an empty /
    /// unparsed TeamPosition, or a remake) get no opponent.
    /// </summary>
    private static Dictionary<int, MatchParticipant?> BuildOpponentMap(
        List<MatchParticipant> participants)
    {
        var map = new Dictionary<int, MatchParticipant?>(participants.Count);
        foreach (var p in participants)
        {
            map[p.ParticipantId] = string.IsNullOrEmpty(p.TeamPosition)
                ? null
                : participants.FirstOrDefault(o =>
                    o.TeamId != p.TeamId && o.TeamPosition == p.TeamPosition);
        }

        return map;
    }

    private static MatchDetailLaning15ReadModel? ComputeLaning15(
        int participantId,
        MatchParticipant? opponent,
        Dictionary<int, Snapshot15> snapshotByParticipant)
    {
        if (opponent is null)
        {
            return null;
        }

        if (!snapshotByParticipant.TryGetValue(participantId, out var self)
            || !snapshotByParticipant.TryGetValue(opponent.ParticipantId, out var foe))
        {
            return null;
        }

        return new MatchDetailLaning15ReadModel
        {
            CsDiff = self.Cs - foe.Cs,
            GoldDiff = self.Gold - foe.Gold,
            XpDiff = self.Xp - foe.Xp,
        };
    }

    /// <summary>
    /// True when <paramref name="self"/> hit their 2nd skill point (level 2)
    /// strictly before their lane opponent. Null when there is no opponent or
    /// either side has fewer than two skill events.
    /// </summary>
    private static bool? ComputeFirstToLevelTwo(
        MatchParticipant self,
        MatchParticipant? opponent)
    {
        if (opponent is null)
        {
            return null;
        }

        var selfLevel2 = LevelTwoTimestamp(self);
        var foeLevel2 = LevelTwoTimestamp(opponent);
        if (selfLevel2 is null || foeLevel2 is null)
        {
            return null;
        }

        return selfLevel2.Value < foeLevel2.Value;
    }

    private static int? LevelTwoTimestamp(MatchParticipant p)
    {
        // The 2nd skill level-up event is the moment the champion reached level
        // 2. SkillEvents are stored in skill-up order; sort by timestamp to be
        // safe before taking the second.
        var ordered = p.SkillEvents
            .OrderBy(e => e.TimestampMs)
            .ToList();
        return ordered.Count >= 2 ? ordered[1].TimestampMs : null;
    }

    /// <summary>@15 timeline projection: combined cs, gold and xp for a participant.</summary>
    private sealed record Snapshot15(int ParticipantId, int Cs, int Gold, int Xp);

    /// <summary>One rune selection row: owning style, slot index and perk id.</summary>
    private sealed record PerkRow(int ParticipantId, int StyleId, int SelectionIndex, int PerkId);
}
