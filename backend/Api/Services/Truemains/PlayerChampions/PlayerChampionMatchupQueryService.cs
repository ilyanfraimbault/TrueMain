using Core.Lol.Map;
using Core.Options;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.PlayerChampions;

/// <summary>
/// One player's lane matchups on a champion, in the same <see cref="ChampionMatchupsResponse"/>
/// the global matchups endpoint returns. Computed live, self-joining
/// <c>match_participants</c> to pair the player's row with its lane opponent (same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>, same match): the matchup aggregate carries no
/// account dimension.
/// </summary>
public interface IPlayerChampionMatchupQueryService
{
    /// <summary>
    /// Returns the player's lane matchups in <paramref name="scope"/>: its lane, or
    /// <see cref="LanePositions.AllPositions"/> — each game then pairs with the opponent of the
    /// position the player held in it. Every opponent the player faced is listed, unfloored.
    /// <see langword="null"/> means the name tag is malformed or no account matches — the
    /// controller maps that to 404. <paramref name="opponentChampionId"/> narrows to one
    /// head-to-head.
    /// </summary>
    Task<ChampionMatchupsResponse?> GetAsync(
        string nameTag,
        ChampionScope scope,
        int? opponentChampionId,
        CancellationToken ct);
}

public sealed class PlayerChampionMatchupQueryService(
    TruemainAccountResolver resolver,
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IChampionReadCache cache) : IPlayerChampionMatchupQueryService
{
    public async Task<ChampionMatchupsResponse?> GetAsync(
        string nameTag,
        ChampionScope scope,
        int? opponentChampionId,
        CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        var position = scope.RequiredPosition;
        var normalizedPatch = scope.Patch;

        // The live self-join was the uncached half of the matchup read before #1368.
        var matchups = await cache.GetOrComputeAsync(
            $"champions:matchups:player:{account.Id}:{scope.CacheToken()}:{opponentChampionId?.ToString() ?? "any"}",
            token => ComputeAsync(scope.ChampionId, position, normalizedPatch, account.Id, opponentChampionId, token),
            ct);

        return new ChampionMatchupsResponse
        {
            ChampionId = scope.ChampionId,
            Position = position,
            Patch = normalizedPatch,
            Matchups = matchups,
        };
    }

    // Player-scoped matchups are one player's own games: a rank filter is meaningless there,
    // so the route takes none and the join reads every band.
    private async Task<List<ChampionMatchupEntry>> ComputeAsync(
        int championId,
        string position,
        string? normalizedPatch,
        Guid riotAccountId,
        int? opponentChampionId,
        CancellationToken ct)
    {
        // Same queue cast the sibling champion reads use, so the matchup slice
        // is drawn from the same population as the build / summary pages.
        var queueId = (int)options.Value.QueueId;

        // No games floor on one player's own matchups: every opponent they faced is
        // listed, from a single game up. A player meets most opponents once or twice,
        // so any floor empties the list, and what a reader looks for here is their own
        // history against a pick, however thin — the row carries its game count. The
        // global leaderboard keeps its floors; this is a different question.

        // The champion side: rows for this champion at this position (every lane
        // position under ALL), narrowed to the one account asked for, in the games the
        // cohort counts on the configured queue and patch (matched via the correlated
        // EXISTS over matches) — one player's own games, but never a remake.
        var games = ChampionCohort.Games(db, queueId, normalizedPatch);
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId)
            .Where(p1 => p1.RiotAccountId == riotAccountId)
            .Where(p1 => games.Any(m => m.Id == p1.MatchId));
        championRows = string.Equals(position, LanePositions.AllPositions, StringComparison.Ordinal)
            // Only a real lane position pairs with a role opponent: an unassigned
            // row would pair with the other team's unassigned one.
            ? championRows.Where(p1 => LanePositions.All.Contains(p1.TeamPosition))
            : championRows.Where(p1 => p1.TeamPosition == position);

        // Correlate each champion row to its lane opponent: same match, same
        // position, opposite team. Kept whole — no opponent narrowing, no floor —
        // because this is also the player's own opponent field, which the play rate
        // below is a share of.
        var opponentPairs = championRows
            .SelectMany(
                p1 => db.MatchParticipants.Where(p2 =>
                    p2.MatchId == p1.MatchId
                    && p2.TeamPosition == p1.TeamPosition
                    && p2.TeamId != p1.TeamId),
                (p1, p2) => new
                {
                    p1.MatchId,
                    SelfParticipantId = p1.ParticipantId,
                    OpponentParticipantId = p2.ParticipantId,
                    Opponent = p2.ChampionId,
                    p1.Win,
                });

        // The player's whole field on this champion and lane, counted rather than
        // summed from the rows below: those are narrowed to one opponent on a search
        // (dividing the row by itself and calling it 100%) and already floored on the
        // leaderboard (inflating every share by the dropped tail). One indexed COUNT,
        // no rows materialised — the same second read the aggregate path pays (#1098),
        // which the search here used to skip by reporting a play rate of 0.
        var totalGames = await opponentPairs.LongCountAsync(ct);

        // The player's games themselves, narrowed to the requested opponent: one
        // player on one champion is a few hundred rows at most, so they are read once
        // and grouped here — the lane half needs each game's pair of participants, which
        // a SQL GROUP BY would fold away.
        var pairs = await opponentPairs
            .Where(x => opponentChampionId == null || x.Opponent == opponentChampionId)
            .ToListAsync(ct);

        // The lane half from the player's own 15-minute snapshots, never the aggregate's
        // counters: those are folded over every tracked account, and lending them to one
        // player's row would report the population's lane as theirs.
        var lanes = await PlayerLaneOutcomes.TallyAsync(
            db,
            pairs.Select(x => new PlayerLanePair(x.MatchId, x.SelfParticipantId, x.OpponentParticipantId, x.Opponent))
                .ToList(),
            ct);

        // One line per opponent champion — across positions under ALL — and no lane
        // floor either: like the games, a decided lane is the player's own history
        // however thin, and the row carries its count.
        return MatchupEntries.ToOrdered(
            pairs
                .GroupBy(x => x.Opponent)
                .Select(g => new MatchupRow(
                    g.Key,
                    g.Count(),
                    g.Count(x => x.Win),
                    totalGames,
                    lanes.TryGetValue(g.Key, out var lane)
                        ? new LaneOutcome(
                            lane.Wins, lane.Losses, lane.GoldDiffSum, lane.Measured, lane.XpDiffSum, lane.Measured)
                        : null)),
            minDecidedLanes: 1);
    }
}
