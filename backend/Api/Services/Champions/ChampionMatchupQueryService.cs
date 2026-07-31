using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Champion lane-matchups query. The global leaderboard slice is served from the
/// pre-aggregated <c>champion_matchup_stats</c> table (#606): one indexed read,
/// folded to the requested patch scope with the games floor applied on the merged
/// total. The player-scoped and opponent-search slices stay live — they self-join
/// <c>match_participants</c> to pair the champion with its lane opponent (same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>, same match), because they need
/// per-account filtering / a sub-floor the aggregate does not carry.
///
/// <para>
/// The two sources meet on the global opponent search (#976): its games and wins stay
/// live so a one-game head-to-head still shows, while its lane counters — win rate and
/// average gold gap at 15 minutes, neither computable without the timeline — are read
/// from the aggregate row for that opponent. The player-scoped search keeps them null
/// rather than lending it the population's lane.
/// </para>
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
        string? eloBracket,
        CancellationToken ct)
    {
        // Canonicalise to major.minor (e.g. "16.4.521.123" → "16.4"). The
        // interface contract accepts either form, so the service normalises its
        // own input and stays correct standalone. Null / unparseable input means
        // "every patch".
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause). Applied to
        // the champion side on both the aggregate and the live paths.
        var bands = EloBracket.ResolveFilter(eloBracket);

        // The global slice (no player, no opponent) is the only one backed by the
        // aggregate. The other two stay live: an opponent lookup wants the
        // head-to-head from a single game (floor 1), and a player slice filters to
        // one account with a lower floor — neither is expressible against a
        // global, floor-free aggregate.
        var matchups = opponentChampionId is null && riotAccountId is null
            ? await ReadFromAggregateAsync(championId, position, normalizedPatch, bands, ct)
            : await ComputeLiveAsync(championId, position, normalizedPatch, riotAccountId, opponentChampionId, bands, ct);

        return new ChampionMatchupsResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Matchups = matchups,
        };
    }

    private async Task<List<ChampionMatchupEntry>> ReadFromAggregateAsync(
        int championId,
        string position,
        string? normalizedPatch,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        var minGames = championsOptions.Value.MinMatchupGames;

        // Rows are stored per (opponent, patch, band) with no floor. Fold to the
        // requested scope — one patch, or every patch summed; the requested elo
        // bands, or every band — then apply the floor on the merged total so the
        // all-patches view floors on the real total, not on any single slice.
        var query = db.ChampionMatchupStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId && s.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }
        if (bands is not null)
        {
            query = query.Where(s => bands.Contains(s.EloBracket));
        }

        var rows = await query
            .GroupBy(s => s.OpponentChampionId)
            .Select(g => new
            {
                Opponent = g.Key,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
                // Summed over the same folded scope as games/wins, so the lane figure
                // describes the same slice as the row's win rate (#919).
                LaneWins = g.Sum(x => x.LaneWins),
                LaneLosses = g.Sum(x => x.LaneLosses),
                // The gap behind those outcomes (#976), carrying its own denominator:
                // rows folded before it shipped contribute outcomes and no gap, and
                // summing both keeps the average over exactly what was measured.
                GoldDiffSum = g.Sum(x => x.LaneGoldDiffSum),
                GoldDiffGames = g.Sum(x => x.LaneGoldDiffGames),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        return ToOrderedEntries(rows.Select(x => (
            x.Opponent,
            x.Games,
            x.Wins,
            LaneOutcome: (LaneOutcome?)new LaneOutcome(
                x.LaneWins, x.LaneLosses, x.GoldDiffSum, x.GoldDiffGames))));
    }

    /// <summary>
    /// Lane counters for one opponent, read straight from the aggregate over the same
    /// patch / elo scope the caller asked for — the half the live opponent search cannot
    /// compute for itself (#976).
    ///
    /// <para>
    /// Global slice only. The aggregate is folded over the whole tracked population, so
    /// pinning it onto a player-scoped row would tell one player their lane went the way
    /// everybody else's did. The caller keeps lane data null there instead.
    /// </para>
    /// </summary>
    private async Task<LaneOutcome?> ReadOpponentLaneOutcomeAsync(
        int championId,
        string position,
        int opponentChampionId,
        string? normalizedPatch,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        var query = db.ChampionMatchupStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId
                && s.TeamPosition == position
                && s.OpponentChampionId == opponentChampionId);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }
        if (bands is not null)
        {
            query = query.Where(s => bands.Contains(s.EloBracket));
        }

        // No floor and no grouping key: the head-to-head is already one opponent, and
        // the rows only need folding across the patch / band slices in scope.
        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                LaneWins = g.Sum(x => x.LaneWins),
                LaneLosses = g.Sum(x => x.LaneLosses),
                GoldDiffSum = g.Sum(x => x.LaneGoldDiffSum),
                GoldDiffGames = g.Sum(x => x.LaneGoldDiffGames),
            })
            .FirstOrDefaultAsync(ct);

        return totals is null
            ? null
            : new LaneOutcome(totals.LaneWins, totals.LaneLosses, totals.GoldDiffSum, totals.GoldDiffGames);
    }

    private async Task<List<ChampionMatchupEntry>> ComputeLiveAsync(
        int championId,
        string position,
        string? normalizedPatch,
        Guid? riotAccountId,
        int? opponentChampionId,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        // Same queue cast the sibling champion reads use, so the matchup slice
        // is drawn from the same population as the build / summary pages.
        var queueId = (int)options.Value.QueueId;

        // The matches table stores the full Riot GameVersion, so an exact compare
        // would never hit; the LIKE prefix bridges normalised input to it.
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        // Floor matrix for the two live slices. A deliberate opponent lookup shows
        // the head-to-head from a single game up (floor 1); a player leaderboard
        // keeps the lower per-player floor.
        var minGames = opponentChampionId is not null
            ? 1
            : championsOptions.Value.MinPlayerMatchupGames;

        // The champion side of the lane: rows for this champion at this
        // position, on the configured queue (matched via the correlated
        // EXISTS over matches), optionally narrowed to one player.
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
        // A player-scoped call narrows to one account.
        championRows = riotAccountId is { } accountId
            ? championRows.Where(p1 => p1.RiotAccountId == accountId)
            : championRows.Where(p1 => p1.RiotAccountId != null);

        // Narrow the champion side to the requested elo bands (null = every band).
        if (bands is not null)
        {
            championRows = championRows.Where(p1 => bands.Contains(p1.EloBracket));
        }

        // One SQL round-trip: correlate each champion row to its lane opponent
        // (same match + position, opposite team), group by the opponent
        // champion, and COUNT(*) / SUM(win) per opponent. The minimum-games floor
        // is applied in SQL (HAVING) so thin samples never cross the wire.
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

        // The lane half never comes from this query: joining the 15-minute snapshots
        // live would reintroduce the per-request timeline scan #606 moved into an
        // aggregate. A deliberate *global* opponent lookup reads the pre-folded row
        // for that one opponent instead (#976) — one indexed seek, no scan.
        //
        // A player-scoped slice keeps it null. The aggregate covers every tracked
        // account, so lending it to one player's row would report the population's
        // lane as theirs; unknown is the honest answer until the fold is player-aware.
        var laneOutcome = opponentChampionId is { } opponent && riotAccountId is null
            ? await ReadOpponentLaneOutcomeAsync(championId, position, opponent, normalizedPatch, bands, ct)
            : null;

        return ToOrderedEntries(rows.Select(x => (x.Opponent, x.Games, x.Wins, LaneOutcome: laneOutcome)));
    }

    /// <summary>
    /// Shared final projection for both read paths: materialised
    /// (opponent, games, wins) rows — every one already above its floor, so
    /// games is never zero — mapped to entries ordered best-winrate first.
    /// </summary>
    private static List<ChampionMatchupEntry> ToOrderedEntries(
        IEnumerable<(int Opponent, int Games, int Wins, LaneOutcome? LaneOutcome)> rows)
        => rows
            .Select(x => new ChampionMatchupEntry
            {
                OpponentChampionId = x.Opponent,
                Games = x.Games,
                Wins = x.Wins,
                WinRate = RateMath.Rate(x.Wins, x.Games),
                // Decided lanes only. Zero decided lanes yields null rather than 0%:
                // "no lane was ever settled here" and "the lane is always lost" are
                // different facts and must not render alike.
                DecidedLaneGames = x.LaneOutcome?.Decided ?? 0,
                LaneWinRate = x.LaneOutcome is { Decided: > 0 } outcome
                    ? RateMath.Rate(outcome.Wins, outcome.Decided)
                    : null,
                // Averaged over the lanes the gap was measured on — never over decided
                // lanes or games, both of which are larger and would drag the average
                // toward zero by the share of lanes nobody ever measured (#976).
                GoldDiffLaneGames = x.LaneOutcome?.GoldDiffGames ?? 0,
                AverageGoldDiffAt15 = x.LaneOutcome is { GoldDiffGames: > 0 } gap
                    ? (double)gap.GoldDiffSum / gap.GoldDiffGames
                    : null,
            })
            .OrderByDescending(m => m.WinRate)
            .ToList();

    /// <summary>
    /// Lane wins and losses past the threshold (evens are in neither), plus the summed
    /// gold gap at 15 minutes over its own sample — smaller than <see cref="Decided"/>
    /// on rows folded before #976, and the reason the two carry separate denominators.
    /// </summary>
    private readonly record struct LaneOutcome(int Wins, int Losses, long GoldDiffSum, int GoldDiffGames)
    {
        public int Decided => Wins + Losses;
    }
}
