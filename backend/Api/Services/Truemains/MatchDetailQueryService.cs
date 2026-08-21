using Core.Lol.Map;
using Core.Lol.Performance;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Read path for the single-match detail page
/// (<c>GET /truemains/{nameTag}/matches/{matchId}</c>). Loads the match header,
/// all 10 participants with their build order / skill order / rune page, the
/// timeline snapshots at every canonical mark, the match's early kill positions,
/// the junglers' first-clear measurements (#1188)
/// and a temporally-nearest rank snapshot per tracked account — then computes
/// the derived per-minute rates, laning diffs and the performance score /
/// placement / MVP / ACE accolades server-side so the frontend renders them
/// directly.
///
/// Scope per issues #523 and #639: no team objectives and no ward counts — only
/// data the DB already has. The performance score is a derived metric
/// (<see cref="PerformanceScore"/>), so it needs no schema of its own; its
/// inputs are assembled by <see cref="PerformanceInputs"/>, shared with the
/// match-history feed so a collapsed row and this payload cannot disagree.
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

        // Timeline snapshots at every canonical mark (5/10/15/20/30) — one per
        // participant per mark when present. They feed both the `laning15`
        // payload field and the lead components of the performance score, which
        // grade the whole 5→30 curve rather than the single @15 point.
        // Projected to scalars and shaped into TimelineMark in memory: the
        // struct never has to survive an EF projection.
        var snapshots = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => s.MatchId == matchId)
            .Select(s => new
            {
                s.ParticipantId,
                s.IntervalMinute,
                Cs = s.MinionsKilled + s.JungleMinionsKilled,
                s.TotalGold,
                s.Xp,
            })
            .ToListAsync(ct);

        // (participant, minute) is unique at the schema level; GroupBy keeps the
        // dictionary build tolerant of an anomalous duplicate rather than 500-ing.
        var marksByKey = snapshots
            .GroupBy(s => (s.ParticipantId, Minute: s.IntervalMinute))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var row = g.First();
                    return new TimelineMark(
                        row.ParticipantId, row.IntervalMinute, row.Cs, row.TotalGold, row.Xp);
                });

        // Early kill participations with a map position. Bounded by the ingestor
        // to the early game, so the whole set is the roam window. An empty set
        // means the match has no coverage at all, which drops the roam component
        // instead of scoring every player a 0.
        var killRows = await db.MatchParticipantKillPositions
            .AsNoTracking()
            .Where(k => k.MatchId == matchId)
            .Select(k => new { k.ParticipantId, k.X, k.Y })
            .ToListAsync(ct);

        var killSpots = killRows
            .Select(k => new KillSpot(k.ParticipantId, k.X, k.Y))
            .ToList();

        // First-clear rows for this match's junglers (#1188, usually 0–2 rows).
        // Samples is a jsonb collection, so the rows are materialized like the
        // participants. GroupBy keeps an anomalous duplicate from 500-ing,
        // mirroring the snapshot dictionary above.
        var jungleClearByParticipant = (await db.JungleFirstClears
                .AsNoTracking()
                .Where(j => j.MatchId == matchId)
                .ToListAsync(ct))
            .GroupBy(j => j.ParticipantId)
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

        // Team kills per side, for the displayed KP%. The share components of the
        // score fold their own totals inside the shared input builder.
        var teamKills = participants
            .GroupBy(p => p.TeamId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Kills));

        var durationMinutes = match.GameDurationSeconds > 0
            ? match.GameDurationSeconds / 60d
            : 0d;

        // Lane opponent = the participant on the other team sharing the same
        // non-empty TeamPosition. Built once; used here for first-to-2 (the
        // shared input builder resolves the same pairing for the lead curve).
        var opponentByParticipant = BuildOpponentMap(participants);

        // Scoring inputs for all 10 participants, through the one builder every
        // scoring surface shares. The payload's `laning15` is the @15 point of
        // the lead curve the score itself consumes, so the two cannot diverge.
        var scoringInputs = PerformanceInputs.BuildMatchInputs(
            participants
                .Select(p => new ScoredParticipant(
                    p.ParticipantId,
                    p.TeamId,
                    p.TeamPosition,
                    p.Win,
                    p.Kills,
                    p.Deaths,
                    p.Assists,
                    p.TotalMinionsKilled + p.NeutralMinionsKilled,
                    p.TotalDamageDealtToChampions,
                    p.GoldEarned,
                    p.VisionScore))
                .ToList(),
            match.GameDurationSeconds,
            marksByKey,
            killSpots);

        var laning15ByParticipant = scoringInputs.ToDictionary(
            built => built.Participant.ParticipantId,
            built => Laning15Of(built.Input.LaneLeads));

        // Performance score for all 10 participants, then the match-wide
        // placement (1..10) and the MVP / ACE accolades derived from it.
        var scoreByParticipant = scoringInputs.ToDictionary(
            built => built.Participant.ParticipantId,
            built => PerformanceScore.Compute(built.Input));

        var placementByParticipant = MatchPerformanceRanker.Rank(participants
            .Select(p => new MatchPerformanceEntry
            {
                ParticipantId = p.ParticipantId,
                Win = p.Win,
                Score = scoreByParticipant[p.ParticipantId],
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
            }));

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

                var laning15 = laning15ByParticipant[p.ParticipantId];
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

                MatchDetailJungleClearReadModel? jungleClear = null;
                if (jungleClearByParticipant.TryGetValue(p.ParticipantId, out var clear))
                {
                    // Drop samples sitting at the map origin. A real frame never
                    // lands there (the map starts at -120 and the blue fountain is
                    // at ~554,581), so (0, 0) is the deserialization signature of a
                    // pre-#1188 camp-sequence document: the reshape migration wipes
                    // those rows, but an ingestor transaction already in flight when
                    // it ran commits rows the DELETE never saw, and the column
                    // rename then carries them into the new shape. Observed on
                    // preprod (12 rows). Such a row would otherwise plot every
                    // position in the top-left corner.
                    var samples = clear.Samples
                        .Where(s => s.X != 0 || s.Y != 0)
                        .OrderBy(s => s.TimestampMs)
                        .Select(s => new MatchDetailJungleClearSampleReadModel
                        {
                            TimestampMs = s.TimestampMs,
                            CampsCleared = JungleCamps.CampsCleared(s.JungleCs),
                            JungleCs = s.JungleCs,
                            X = s.X,
                            Y = s.Y,
                        })
                        .ToList();

                    if (samples.Count > 0)
                    {
                        // Derived from the samples, never stored (#1193): a second
                        // copy is what let the full-clear time keep the old
                        // five-camp threshold after #1191 corrected it.
                        var fullClear = samples
                            .Where(s => s.JungleCs >= JungleCamps.FullClearJungleCs)
                            .Select(s => (int?)s.TimestampMs)
                            .FirstOrDefault();

                        jungleClear = new MatchDetailJungleClearReadModel
                        {
                            StartCamp = clear.StartCamp,
                            Samples = samples,
                            FullClearTimeMs = fullClear,
                            FullClearCamps = JungleCamps.FullClearCamps,
                        };
                    }
                }

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
                    PerformanceScore = scoreByParticipant[p.ParticipantId],
                    Placement = placementByParticipant[p.ParticipantId].Placement,
                    IsMvp = placementByParticipant[p.ParticipantId].IsMvp,
                    IsAce = placementByParticipant[p.ParticipantId].IsAce,
                    Laning15 = laning15,
                    FirstToLevelTwo = firstToTwo,
                    Runes = runes,
                    StatPerkOffense = p.PerksOffense,
                    StatPerkFlex = p.PerksFlex,
                    StatPerkDefense = p.PerksDefense,
                    ItemEvents = itemEvents,
                    SkillEvents = skillEvents,
                    JungleClear = jungleClear,
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
    /// Maps each participant id to its lane opponent — the participant on the
    /// other team sharing the same non-empty <c>TeamPosition</c>, and only when
    /// there is exactly one. Positions with anything else (an empty / unparsed
    /// TeamPosition, a remake, or anomalous data putting two enemies on one
    /// position) get no opponent.
    ///
    /// <para>The "exactly one" rule is enforced here, not assumed, and matches
    /// <see cref="PerformanceInputs.FindLaneOpponent"/> — so the first-to-level-2
    /// flag and the score's lead curve always talk about the same duel.</para>
    /// </summary>
    private static Dictionary<int, MatchParticipant?> BuildOpponentMap(
        List<MatchParticipant> participants)
    {
        var map = new Dictionary<int, MatchParticipant?>(participants.Count);
        foreach (var p in participants)
        {
            if (string.IsNullOrEmpty(p.TeamPosition))
            {
                map[p.ParticipantId] = null;
                continue;
            }

            MatchParticipant? found = null;
            var ambiguous = false;
            foreach (var o in participants)
            {
                if (o.TeamId == p.TeamId || o.TeamPosition != p.TeamPosition)
                {
                    continue;
                }

                if (found is not null)
                {
                    ambiguous = true;
                    break;
                }

                found = o;
            }

            map[p.ParticipantId] = ambiguous ? null : found;
        }

        return map;
    }

    /// <summary>
    /// Projects the @15 point out of the full lead curve for the payload's
    /// <c>laning15</c> field. Null when that mark is not covered on both sides,
    /// even if earlier or later marks are.
    /// </summary>
    private static MatchDetailLaning15ReadModel? Laning15Of(IReadOnlyList<LaneLead> leads)
    {
        foreach (var lead in leads)
        {
            if (lead.Minute == LaningIntervalMinute)
            {
                return new MatchDetailLaning15ReadModel
                {
                    CsDiff = lead.CsDiff,
                    GoldDiff = lead.GoldDiff,
                    XpDiff = lead.XpDiff,
                };
            }
        }

        return null;
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

    /// <summary>One rune selection row: owning style, slot index and perk id.</summary>
    private sealed record PerkRow(int ParticipantId, int StyleId, int SelectionIndex, int PerkId);
}
