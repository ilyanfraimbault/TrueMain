using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Live champion lane-matchups query. Self-joins <c>match_participants</c> to
/// pair a champion with every lane opponent it met (same <c>TeamPosition</c>,
/// opposite <c>TeamId</c>, same match), joins through to <c>matches</c> for the
/// queue / patch filter the other champion reads share, and folds each opponent
/// down to a game count and a win count. There is no aggregation table — the
/// numbers are computed from the raw participant rows on every request.
/// </summary>
public sealed class ChampionMatchupQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions)
    : IChampionMatchupQueryService
{
    public async Task<ChampionMatchupsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        Guid? riotAccountId,
        int? opponentChampionId,
        CancellationToken ct)
    {
        // Same queue cast the sibling champion reads use, so the matchup slice
        // is drawn from the same population as the build / summary pages.
        var queueId = (int)options.Value.QueueId;

        // Canonicalise to major.minor (e.g. "16.4.521.123" → "16.4"). The
        // interface contract accepts either form, so the service normalises its
        // own input and stays correct standalone — both controllers happen to
        // pre-normalise, but the service doesn't rely on that. The matches table
        // stores the full Riot GameVersion, so an exact compare would never hit;
        // the LIKE prefix below bridges the two forms. Null / unparseable input
        // means "every patch".
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToString() : null;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        // Floor matrix. A deliberate opponent lookup shows the head-to-head from
        // a single game up (floor 1); the best/worst leaderboard keeps a sample
        // floor so a one-game fluke never tops it — the lower per-player floor
        // for a player slice, the global floor otherwise.
        var minGames = opponentChampionId is not null
            ? 1
            : riotAccountId is not null
                ? championsOptions.Value.MinPlayerMatchupGames
                : championsOptions.Value.MinMatchupGames;

        // The champion side of the lane: rows for this champion at this
        // position, on the configured queue (matched via the correlated
        // EXISTS over matches), optionally narrowed to one player. Each row's
        // lane opponent is paired in the SelectMany below.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId && p1.TeamPosition == position)
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null
                    || EF.Functions.Like(m.GameVersion, patchPrefix!))));

        // Scope the champion side to tracked accounts so the matchup pool matches
        // the champion page's aggregation, which only counts tracked truemains —
        // never the untracked random players who merely shared a truemain's game.
        // Including them drags in off-meta lane picks and inflates the counts past
        // the champion's own total. A player-scoped call narrows to one account.
        championRows = riotAccountId is { } accountId
            ? championRows.Where(p1 => p1.RiotAccountId == accountId)
            : championRows.Where(p1 => p1.RiotAccountId != null);

        // One SQL round-trip: correlate each champion row to its lane opponent
        // (same match + position, opposite team), group by the opponent
        // champion, and COUNT(*) / SUM(win) per opponent. EF translates this
        // SelectMany-then-GroupBy to a self-join with a GROUP BY over the
        // opponent champion id (verified at runtime against Postgres). The
        // minimum-games floor is applied in SQL (HAVING) so thin samples never
        // cross the wire.
        var rows = await championRows
            .SelectMany(
                p1 => db.MatchParticipants.Where(p2 =>
                    p2.MatchId == p1.MatchId
                    && p2.TeamPosition == p1.TeamPosition
                    && p2.TeamId != p1.TeamId
                    && (opponentChampionId == null || p2.ChampionId == opponentChampionId)),
                (p1, p2) => new { Opponent = p2.ChampionId, p1.Win })
            .GroupBy(x => x.Opponent)
            .Select(g => new
            {
                Opponent = g.Key,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        // Materialise the entries and order by win rate descending. Games is
        // guaranteed >= MinMatchupGames here (the HAVING dropped the rest), so
        // no divide-by-zero guard is needed.
        var matchups = rows
            .Select(x => new ChampionMatchupEntry
            {
                OpponentChampionId = x.Opponent,
                Games = x.Games,
                Wins = x.Wins,
                WinRate = (double)x.Wins / x.Games,
            })
            .OrderByDescending(m => m.WinRate)
            .ToList();

        return new ChampionMatchupsResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Matchups = matchups,
        };
    }
}
