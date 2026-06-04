using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Live champion lane-matchup query. Self-joins <c>match_participants</c> to
/// pair a champion with its lane opponent (same <c>TeamPosition</c>, opposite
/// <c>TeamId</c>, same match), joins through to <c>matches</c> for the queue /
/// patch filter the other champion reads share, and folds the slice down to a
/// game count and a win count. There is no aggregation table — the numbers are
/// computed from the raw participant rows on every request.
/// </summary>
public sealed class ChampionMatchupQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions)
    : IChampionMatchupQueryService
{
    public async Task<ChampionMatchupResponse?> GetAsync(
        int championId,
        string position,
        int opponentChampionId,
        string? patch,
        Guid? riotAccountId,
        CancellationToken ct)
    {
        // Same queue cast the sibling champion reads use, so the matchup slice
        // is drawn from the same population as the build / summary pages.
        var queueId = (int)options.Value.QueueId;

        // Canonicalise to major.minor (e.g. "16.4.521.123" → "16.4"). The
        // matches table stores the full Riot GameVersion, so an exact compare
        // would never hit; the LIKE prefix below bridges the two forms. Null /
        // unparseable input means "every patch".
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToString() : null;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        // The champion side of the lane: rows for this champion at this
        // position, on the configured queue, optionally narrowed to one
        // player. The opposite side is matched per-row in the EXISTS below.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId && p1.TeamPosition == position);

        if (riotAccountId is { } accountId)
        {
            championRows = championRows.Where(p1 => p1.RiotAccountId == accountId);
        }

        // The champion's rows that both (a) sit on a match passing the queue /
        // patch filter and (b) had the requested opponent on the other side of
        // the same lane. Expressed as two correlated EXISTS so EF emits a flat
        // SELECT … WHERE EXISTS(…) AND EXISTS(…) over match_participants.
        var matchupRows = championRows
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null
                    || EF.Functions.Like(m.GameVersion, patchPrefix!))))
            .Where(p1 => db.MatchParticipants.Any(p2 =>
                p2.MatchId == p1.MatchId
                && p2.TeamPosition == p1.TeamPosition
                && p2.TeamId != p1.TeamId
                && p2.ChampionId == opponentChampionId));

        // One SQL round-trip: COUNT(*) and SUM(win) over the matchup set in a
        // single grouped aggregate. Scalar aggregates the translator handles
        // without the read-model projection limits the build / summary reads
        // have to dance around.
        var totals = await matchupRows
            .GroupBy(_ => 1)
            .Select(g => new { Games = g.Count(), Wins = g.Sum(p => p.Win ? 1 : 0) })
            .FirstOrDefaultAsync(ct);
        var games = totals?.Games ?? 0;
        var wins = totals?.Wins ?? 0;

        // Minimum-games floor: a thin head-to-head sample is noise, so report
        // "not enough data" (null → 404) rather than a win rate inferred from a
        // game or two. The floor is a product knob (ChampionsList options) and
        // applies to global and player-scoped callers alike.
        if (games < championsOptions.Value.MinMatchupGames)
        {
            return null;
        }

        return new ChampionMatchupResponse
        {
            ChampionId = championId,
            OpponentChampionId = opponentChampionId,
            Position = position,
            Patch = normalizedPatch,
            Games = games,
            Wins = wins,
            // games is guaranteed >= MinMatchupGames here (the floor returned
            // null above), so no divide-by-zero guard is needed.
            WinRate = (double)wins / games,
        };
    }
}
