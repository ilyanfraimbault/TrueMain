using Core.Lol.Performance;
using Data.Entities;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.PlayerChampions;

namespace TrueMain.Services.Truemains.Matches;

/// <summary>
/// The projection half of the single-match detail page: turns the rows
/// <see cref="MatchDetailQueryService"/> loaded into the ten
/// <see cref="MatchDetailParticipantReadModel"/>s, computing the derived
/// per-minute rates, KP%, laning diffs, first-to-level-2 flag, ordered rune
/// page and the performance score / placement / MVP / ACE accolades.
///
/// Pure and database-free — the same split as <see cref="MatchSummaryHydrator"/>
/// for the collapsed row, so what the page shows can be reasoned about (and
/// tested) apart from the reads that feed it. Scoring goes through
/// <see cref="PerformanceInputs"/>, shared with the match-history feed so a
/// collapsed row and this payload cannot disagree.
/// </summary>
internal static class MatchDetailHydrator
{
    private const int LaningIntervalMinute = 15;

    /// <summary>
    /// Builds every participant of one match, ordered by team then participant id.
    /// <paramref name="accountsById"/> and <paramref name="rankByAccount"/> are keyed
    /// by tracked Riot account id; an untracked participant simply gets no name tag
    /// and no rank.
    /// </summary>
    public static List<MatchDetailParticipantReadModel> HydrateParticipants(
        List<MatchParticipant> participants,
        int gameDurationSeconds,
        IReadOnlyList<PerkRow> perkRows,
        IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> marksByKey,
        IReadOnlyDictionary<Guid, MatchDetailRankReadModel> rankByAccount,
        IReadOnlyDictionary<Guid, (string GameName, string? TagLine)> accountsById)
    {
        // Group raw rune rows per participant; the final keystone-first,
        // primary-tree-then-secondary-tree ordering is applied per participant
        // below, where the participant's PrimaryStyleId is known (SelectionIndex
        // resets to 0 within each tree, so it alone can't order across trees).
        var perkRowsByParticipant = perkRows
            .GroupBy(r => r.ParticipantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Team kills per side, for the displayed KP%. The share components of the
        // score fold their own totals inside the shared input builder.
        var teamKills = participants
            .GroupBy(p => p.TeamId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Kills));

        var durationMinutes = gameDurationSeconds > 0
            ? gameDurationSeconds / 60d
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
            gameDurationSeconds,
            marksByKey);

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

        return participants
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
                    Items = new[] { p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5 },
                    TrinketItemId = p.TrinketItemId,
                    RoleBoundItemId = p.RoleBoundItemId ?? 0,
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
                };
            })
            .ToList();
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
    internal sealed record PerkRow(int ParticipantId, int StyleId, int SelectionIndex, int PerkId);
}
