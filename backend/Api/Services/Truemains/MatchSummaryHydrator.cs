using Core.Lol.Performance;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// One (match, participant) the caller wants a collapsed match row for. The
/// participant is the row's "self": the slice the row renders KDA, items,
/// spells and the performance score from.
/// </summary>
public readonly record struct MatchSummaryKey(string MatchId, int ParticipantId);

/// <summary>
/// Turns a set of (match, participant) keys into collapsed
/// <see cref="MatchSummaryReadModel"/> rows — the hydration half of the
/// truemain match feed, extracted so a second surface can render the same row
/// without duplicating it. Every participant of every requested match is
/// graded with <see cref="PerformanceScore"/>, the same scorer on the same
/// inputs as the single-match detail page, so a row's score, placement and
/// MVP/ACE badge can never disagree with what expanding it shows.
///
/// Two callers today: <see cref="MatchSummariesQueryService"/>
/// (<c>GET /truemains/{nameTag}/matches</c>, keys resolved from the player's
/// puuid) and the composition provenance drawer (#940), whose keys come
/// straight out of the similarity selection and belong to ten different
/// pilots. Cost is bounded by the key count — callers page before hydrating.
/// </summary>
public sealed class MatchSummaryHydrator(
    TrueMainDbContext db,
    ILogger<MatchSummaryHydrator> logger)
{
    public async Task<IReadOnlyDictionary<MatchSummaryKey, MatchSummaryReadModel>> HydrateAsync(
        IReadOnlyCollection<MatchSummaryKey> keys,
        CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return new Dictionary<MatchSummaryKey, MatchSummaryReadModel>();
        }

        var matchIds = keys.Select(k => k.MatchId).Distinct().ToList();
        var selfParticipantIds = keys.Select(k => k.ParticipantId).Distinct().ToList();

        var matchRows = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new MatchRow(
                m.Id,
                m.QueueId,
                m.GameMode,
                m.GameStartTimeUtc,
                m.GameDurationSeconds))
            .ToDictionaryAsync(m => m.Id, ct);

        // All participants across the requested matches — needed for self
        // stats, versus thumbnails, team kills (KP%) and MVP/ACE derivation.
        // One round trip.
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.RiotAccountId,
                p.ChampionId,
                p.ChampLevel,
                p.TeamId,
                p.TeamPosition,
                p.Win,
                p.Kills,
                p.Deaths,
                p.Assists,
                p.TotalMinionsKilled + p.NeutralMinionsKilled,
                p.TotalDamageDealtToChampions,
                p.GoldEarned,
                p.VisionScore,
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

        // Keystone for the SELF participants only: slot 0 of the primary tree.
        // The (matchId, participantId) filter is a rectangle, not the exact
        // set of pairs: any match in matchIds crossed with any id in
        // selfParticipantIds passes, so a caller whose keys span many distinct
        // participant ids (the composition drawer's ten different pilots, one
        // per match) can pull rows for participants nobody asked about — up to
        // the full ~10× fan-out per match the single-account match-feed caller
        // never had. Still correct: the dictionary below is keyed on the exact
        // (match, participant, style) triple, so an over-fetched row is simply
        // never looked up. A caller whose keys are dense across many distinct
        // participant ids that cares about the extra round-trip cost should
        // narrow this to the true pair set (e.g. a per-match dictionary
        // resolved client-side, unioned via multiple round trips) rather than
        // rely on the rectangle staying small. Restricting to
        // cat.StyleId == mp.PrimaryStyleId keeps just the primary tree's
        // keystone — the slot-0 row of the sub tree is skipped since
        // downstream only looks up (…, self.PrimaryStyleId).
        var keystoneRows = await (
            from mp in db.MatchParticipants.AsNoTracking()
            join pps in db.ParticipantPerkSelections.AsNoTracking()
                on new { mp.MatchId, mp.ParticipantId } equals new { pps.MatchId, pps.ParticipantId }
            join cat in db.PerkSelectionCatalogs.AsNoTracking()
                on pps.PerkSelectionCatalogId equals cat.Id
            where matchIds.Contains(mp.MatchId)
                  && selfParticipantIds.Contains(mp.ParticipantId)
                  && cat.SelectionIndex == 0
                  && cat.StyleId == mp.PrimaryStyleId
            select new
            {
                mp.MatchId,
                mp.ParticipantId,
                cat.StyleId,
                cat.PerkId,
            }).ToListAsync(ct);

        // GroupBy keeps the dictionary build duplicate-tolerant.
        // (MatchId, ParticipantId, StyleId) is not unique at the schema level:
        // PerkSelectionCatalog only enforces uniqueness on
        // (StyleId, SelectionIndex, PerkId, StyleDescription), so two slot-0 rows
        // for the same style with distinct PerkId are permitted. A direct
        // ToDictionary would throw ArgumentException (HTTP 500) on such anomalous
        // data. The query has no ORDER BY, so the duplicates come back in
        // Postgres' arbitrary row order; ordering each group by PerkId before
        // First() makes the tiebreak deterministic instead of letting the shown
        // keystone vary between identical requests.
        var keystoneByKey = keystoneRows
            .GroupBy(r => (r.MatchId, r.ParticipantId, r.StyleId))
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.PerkId).First().PerkId);

        // Timeline marks and early kill positions for the whole set, one round
        // trip each. They are the timeline half of the performance score's
        // inputs; both tables are indexed on (MatchId, ParticipantId), and the
        // volume is bounded by the key count (at most ~10 participants × 5 marks
        // per match, and a few dozen kill rows).
        var marks = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId))
            .Select(s => new
            {
                s.MatchId,
                s.ParticipantId,
                s.IntervalMinute,
                Cs = s.MinionsKilled + s.JungleMinionsKilled,
                s.TotalGold,
                s.Xp,
            })
            .ToListAsync(ct);

        var marksByMatch = marks
            .GroupBy(r => r.MatchId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark>)g
                    .GroupBy(r => (r.ParticipantId, Minute: r.IntervalMinute))
                    .ToDictionary(
                        inner => inner.Key,
                        inner =>
                        {
                            var row = inner.First();
                            return new TimelineMark(
                                row.ParticipantId, row.IntervalMinute, row.Cs, row.TotalGold, row.Xp);
                        }));

        var killRows = await db.MatchParticipantKillPositions
            .AsNoTracking()
            .Where(k => matchIds.Contains(k.MatchId))
            .Select(k => new { k.MatchId, k.ParticipantId, k.X, k.Y })
            .ToListAsync(ct);

        var killSpotsByMatch = killRows
            .GroupBy(r => r.MatchId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<KillSpot>)g
                    .Select(r => new KillSpot(r.ParticipantId, r.X, r.Y))
                    .ToList());

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var summaries = new Dictionary<MatchSummaryKey, MatchSummaryReadModel>(keys.Count);
        foreach (var key in keys)
        {
            if (summaries.ContainsKey(key))
            {
                continue;
            }

            if (!matchRows.TryGetValue(key.MatchId, out var match)
                || !participantsByMatch.TryGetValue(key.MatchId, out var partList))
            {
                logger.LogWarning(
                    "match-summary hydration has no participant rows match_id={MatchId}",
                    key.MatchId);
                continue;
            }

            var self = partList.FirstOrDefault(p => p.ParticipantId == key.ParticipantId);
            if (self is null)
            {
                logger.LogWarning(
                    "match-summary hydration missing self participant match_id={MatchId} participant_id={ParticipantId}",
                    key.MatchId, key.ParticipantId);
                continue;
            }

            summaries[key] = BuildSummary(
                match,
                self,
                partList,
                marksByMatch.TryGetValue(match.Id, out var mm) ? mm : PerformanceInputs.NoMarks,
                killSpotsByMatch.TryGetValue(match.Id, out var ks) ? ks : Array.Empty<KillSpot>(),
                keystoneByKey,
                accountsById);
        }

        return summaries;
    }

    private static MatchSummaryReadModel BuildSummary(
        MatchRow match,
        ParticipantRow self,
        List<ParticipantRow> partList,
        IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> matchMarks,
        IReadOnlyList<KillSpot> matchKillSpots,
        IReadOnlyDictionary<(string MatchId, int ParticipantId, int StyleId), int> keystoneByKey,
        IReadOnlyDictionary<Guid, (string GameName, string? TagLine)> accountsById)
    {
        var teamKills = partList
            .Where(p => p.TeamId == self.TeamId)
            .Sum(p => p.Kills);
        var killParticipation = teamKills == 0
            ? 0d
            : (double)(self.Kills + self.Assists) / teamKills;

        // Grade all ten participants with the real scorer, then rank the
        // match. MVP / ACE and the placement are read off that ranking — the
        // row and the detail panel therefore always tell the same story.
        var placements = MatchPerformanceRanker.Rank(ScoreMatch(
            partList, match.GameDurationSeconds, matchMarks, matchKillSpots));

        var selfPlacement = placements[self.ParticipantId];

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
                    // Riot leaves TeamPosition empty on modes without
                    // assigned roles; normalize to null so the JSON stays
                    // a clean tri-state for the frontend.
                    Position = string.IsNullOrEmpty(p.TeamPosition) ? null : p.TeamPosition,
                    GameName = gameName,
                    TagLine = tagLine,
                };
            })
            .ToList();

        return new MatchSummaryReadModel
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
                Position = string.IsNullOrEmpty(self.TeamPosition) ? null : self.TeamPosition,
                Win = self.Win,
                LpDelta = null,
                PerformanceScore = selfPlacement.Score,
                Placement = selfPlacement.Placement,
                IsMvp = selfPlacement.IsMvp,
                IsAce = selfPlacement.IsAce,
            },
            Participants = participantList,
        };
    }

    /// <summary>
    /// Grades every participant of one match through the shared input builder,
    /// so this feed and the detail page score the same game identically.
    /// </summary>
    /// <param name="partList">All participants of the match.</param>
    /// <param name="durationSeconds">Game length in seconds; 0 disables the per-minute components.</param>
    /// <param name="marks">Timeline marks of the match, keyed by (participant, minute).</param>
    /// <param name="killSpots">Early kill participations of the match; empty means no coverage.</param>
    private static IEnumerable<MatchPerformanceEntry> ScoreMatch(
        List<ParticipantRow> partList,
        int durationSeconds,
        IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> marks,
        IReadOnlyList<KillSpot> killSpots)
    {
        var scored = partList
            .Select(p => new ScoredParticipant(
                p.ParticipantId,
                p.TeamId,
                p.TeamPosition,
                p.Win,
                p.Kills,
                p.Deaths,
                p.Assists,
                p.Cs,
                p.DamageToChampions,
                p.GoldEarned,
                p.VisionScore))
            .ToList();

        return PerformanceInputs
            .BuildMatchInputs(scored, durationSeconds, marks, killSpots)
            .Select(built => new MatchPerformanceEntry
            {
                ParticipantId = built.Participant.ParticipantId,
                Win = built.Participant.Win,
                Score = PerformanceScore.Compute(built.Input),
                Kills = built.Participant.Kills,
                Deaths = built.Participant.Deaths,
                Assists = built.Participant.Assists,
            });
    }

    private sealed record MatchRow(
        string Id,
        int QueueId,
        string GameMode,
        DateTime GameStartTimeUtc,
        int GameDurationSeconds);

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        Guid? RiotAccountId,
        int ChampionId,
        int ChampLevel,
        int TeamId,
        string? TeamPosition,
        bool Win,
        int Kills,
        int Deaths,
        int Assists,
        int Cs,
        int DamageToChampions,
        int GoldEarned,
        int VisionScore,
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
