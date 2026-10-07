using Core.Lol.Performance;
using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;
using TrueMain.Services.Truemains.PlayerChampions;

namespace TrueMain.Services.Truemains.Matches;

public interface IMatchDetailQueryService
{
    /// <summary>
    /// Loads the full detail payload for a single match — all 10 participants
    /// with their build order, skill order, rune page and timeline-derived
    /// laning stats. <paramref name="nameTag"/> (<c>gameName-tagLine</c>) is
    /// validated and must resolve to a tracked account, but only scopes the
    /// route; the response covers every participant.
    ///
    /// Returns <c>null</c> when the name tag is malformed, no Riot account
    /// matches, or the match id is unknown / not one this account played in.
    /// </summary>
    Task<MatchDetailReadModel?> GetAsync(string nameTag, string matchId, CancellationToken ct);
}

/// <summary>
/// Read path for the single-match detail page
/// (<c>GET /truemains/{nameTag}/matches/{matchId}</c>). Loads the match header,
/// all 10 participants with their build order / skill order / rune page, the
/// timeline snapshots at every canonical mark, the match's early kill positions,
/// and a temporally-nearest rank snapshot per tracked account — then hands them
/// to <see cref="MatchDetailHydrator"/>, which computes the derived per-minute
/// rates, laning diffs and the performance score / placement / MVP / ACE
/// accolades server-side so the frontend renders them directly.
///
/// Scope per issues #523 and #639: no team objectives and no ward counts — only
/// data the DB already has. The performance score is a derived metric
/// (<see cref="PerformanceScore"/>), so it needs no schema of its own; its
/// inputs are assembled by <see cref="PerformanceInputs"/>, shared with the
/// match-history feed so a collapsed row and this payload cannot disagree.
/// </summary>
public sealed class MatchDetailQueryService(
    TrueMainDbContext db,
    TruemainAccountResolver resolver) : IMatchDetailQueryService
{
    /// <summary>
    /// Half-width of the window the nearest rank snapshot is looked up in. Snapshots are
    /// capped at one per account per UTC day (#907) and never expire, so an unbounded
    /// lookup grows by ~365 rows per account per season, times the ten participants of a
    /// match. Two weeks either side is far wider than the refresh cadence of a followed
    /// account, and an account with nothing in the window falls back to the wide search
    /// below rather than losing its rank badge.
    /// </summary>
    private static readonly TimeSpan RankSnapshotWindow = TimeSpan.FromDays(14);

    public async Task<MatchDetailReadModel?> GetAsync(string nameTag, string matchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matchId))
        {
            return null;
        }

        // The account only scopes the URL — the payload covers every
        // participant — but it must exist and must have actually played this
        // match, so a stray match id under someone else's slug 404s instead of
        // leaking a game the player wasn't in.
        var account = await resolver.ResolveAsync(nameTag, ct);
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
        // primary-tree-then-secondary-tree) is applied by the hydrator.
        var perkRows = await (
            from pps in db.ParticipantPerkSelections.AsNoTracking()
            join cat in db.PerkSelectionCatalogs.AsNoTracking()
                on pps.PerkSelectionCatalogId equals cat.Id
            where pps.MatchId == matchId
            select new MatchDetailHydrator.PerkRow(
                pps.ParticipantId,
                cat.StyleId,
                cat.SelectionIndex,
                cat.PerkId)).ToListAsync(ct);

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
            // rows and reduce in memory — but only the rows inside a window
            // around the game, since a followed account accumulates one snapshot
            // per day forever and only the nearest one is ever kept.
            var rankRows = await LoadRankSnapshotsAsync(
                trackedAccountIds,
                gameStart - RankSnapshotWindow,
                gameStart + RankSnapshotWindow,
                ct);

            // Fallback for the accounts the window missed entirely — a rarely
            // refreshed account can have its closest snapshot months away, and
            // dropping its badge would be a regression, not a saving.
            var coveredAccountIds = rankRows.Select(s => s.RiotAccountId).ToHashSet();
            var uncoveredAccountIds = trackedAccountIds.Where(id => !coveredAccountIds.Contains(id)).ToList();
            if (uncoveredAccountIds.Count > 0)
            {
                rankRows.AddRange(await LoadRankSnapshotsAsync(uncoveredAccountIds, null, null, ct));
            }

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
                            Tier = nearest.Tier.ToRiotName(),
                            Division = nearest.Division.ToRiotName(),
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

        // The win-probability curve (#1911): one row per match, computed at ingest, absent
        // for a game that gets none or was ingested before it existed.
        var winProbability = await db.MatchWinProbabilities
            .AsNoTracking()
            .Where(w => w.MatchId == matchId)
            .FirstOrDefaultAsync(ct);

        var participantModels = MatchDetailHydrator.HydrateParticipants(
            participants,
            match.GameDurationSeconds,
            perkRows,
            marksByKey,
            rankByAccount,
            accountsById);

        return new MatchDetailReadModel
        {
            MatchId = match.Id,
            QueueId = match.QueueId,
            GameMode = match.GameMode,
            GameStartTimeUtc = match.GameStartTimeUtc,
            GameDurationSeconds = match.GameDurationSeconds,
            GameVersion = match.GameVersion,
            Participants = participantModels,
            WinProbability = MatchDetailHydrator.HydrateWinProbability(winProbability),
        };
    }

    /// <summary>
    /// Rank snapshots of the given accounts, optionally restricted to a capture window.
    /// A null bound means "unbounded on that side", which is the fallback the windowed
    /// pass degrades to for an account with nothing nearby.
    /// </summary>
    private async Task<List<RankSnapshotRow>> LoadRankSnapshotsAsync(
        IReadOnlyList<Guid> accountIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken ct)
    {
        return await RankSnapshotsQuery(db, accountIds, fromUtc, toUtc).ToListAsync(ct);
    }

    /// <summary>
    /// The query behind <see cref="LoadRankSnapshotsAsync"/>, exposed so a test can assert
    /// the window really reaches SQL as a range predicate instead of being filtered after
    /// the whole history has been read.
    /// </summary>
    internal static IQueryable<RankSnapshotRow> RankSnapshotsQuery(
        TrueMainDbContext db,
        IReadOnlyList<Guid> accountIds,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        return db.RankSnapshots
            .AsNoTracking()
            .Where(s => accountIds.Contains(s.RiotAccountId)
                && (fromUtc == null || s.CapturedAtUtc >= fromUtc)
                && (toUtc == null || s.CapturedAtUtc <= toUtc))
            .Select(s => new RankSnapshotRow(
                s.RiotAccountId,
                s.CapturedAtUtc,
                s.Tier,
                s.Division,
                s.LeaguePoints));
    }

    internal sealed record RankSnapshotRow(
        Guid RiotAccountId,
        DateTime CapturedAtUtc,
        RankTier Tier,
        RankDivision Division,
        int LeaguePoints);
}
