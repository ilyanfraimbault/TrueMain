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
/// Champion lane-matchups query. Every <em>global</em> slice — the leaderboard and
/// the single-opponent search alike — is served from the pre-aggregated
/// <c>champion_matchup_stats</c> table (#606): one indexed read, folded to the
/// requested patch / elo scope. Only the <em>player-scoped</em> slice stays live,
/// self-joining <c>match_participants</c> to pair the champion with its lane
/// opponent (same <c>TeamPosition</c>, opposite <c>TeamId</c>, same match), because
/// the aggregate carries no account dimension and never will without one.
///
/// <para>
/// <b>The search moved off the live join.</b> It used to self-join for its games and
/// wins while reading its lane counters from the aggregate (#976), on the premise
/// that an aggregate "built at floor 10" could not answer a one-game lookup. The
/// rows were never stored with a floor — only the read applied one — so the premise
/// was wrong, and the split cost real correctness: the live join sees the retention
/// window (two patches of <c>match_participants</c>) while the aggregate keeps every
/// patch it ever folded, so the same matchup answered 22 games on the leaderboard
/// and 13 in the search, and rows came back reporting a gold gap averaged over more
/// lanes than the games they were shown next to. Both halves now come from the same
/// rows.
/// </para>
///
/// <para>
/// <b>Two floors on the leaderboard, none on the search.</b> A deliberate lookup
/// answers with whatever games exist, down to one. The leaderboard drops anything
/// under <c>max(MinMatchupGames, MinMatchupPlayRate × total)</c> — see
/// <see cref="ChampionsListOptions.MinMatchupPlayRate"/> for why an absolute floor
/// alone cannot work across champions three orders of magnitude apart in volume.
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
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);

        // One account means one player's own games, which the aggregate cannot
        // isolate. Everything else reads the aggregate.
        var matchups = riotAccountId is { } accountId
            ? await ComputeLiveAsync(championId, position, normalizedPatch, accountId, opponentChampionId, bands, ct)
            : await ReadFromAggregateAsync(championId, position, normalizedPatch, opponentChampionId, bands, ct);

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
        int? opponentChampionId,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        // Rows are stored per (opponent, patch, band) with no floor. Fold to the
        // requested scope — one patch, or every patch summed; the requested elo
        // bands, or every band — then apply the floors on the merged total so the
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

        // The search narrows to its one opponent in SQL — an indexed seek, and the
        // reason it never pays to materialise the champion's whole opponent field.
        var scopedQuery = opponentChampionId is { } opponent
            ? query.Where(s => s.OpponentChampionId == opponent)
            : query;

        var rows = await scopedQuery
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
                // The experience gap behind the same lanes (#1111), on its own
                // denominator for the same reason the gold gap needed one.
                XpDiffSum = g.Sum(x => x.LaneXpDiffSum),
                XpDiffGames = g.Sum(x => x.LaneXpDiffGames),
            })
            // A row can exist with zero games — the lane fold inserts one when it
            // reaches a matchup before its sibling does — and it is not a matchup
            // anybody played. Dropped here rather than left to divide by zero.
            .Where(x => x.Games > 0)
            .ToListAsync(ct);

        // The champion's whole opponent field, before any floor: the denominator the
        // play-rate floor is a share of, and the one that turns a matchup's games into
        // "how often this matchup actually happens".
        //
        // The search read a single row, so it cannot sum its way to that total — it
        // would be dividing the row by itself and calling it 100%. It used to report a
        // play rate of 0 instead, which the matchup page (#1098) shows as a headline
        // figure, so the total is now a second aggregate over the same scope: one
        // indexed SUM, no rows materialised.
        var totalGames = opponentChampionId is null
            ? rows.Sum(x => (long)x.Games)
            : await query.SumAsync(s => (long)s.Games, ct);

        var qualifying = opponentChampionId is null
            ? rows.Where(x => x.Games >= LeaderboardFloor(totalGames))
            : rows;

        return ToOrderedEntries(qualifying.Select(x => (
            x.Opponent,
            x.Games,
            x.Wins,
            TotalGames: totalGames,
            LaneOutcome: (LaneOutcome?)new LaneOutcome(
                x.LaneWins, x.LaneLosses, x.GoldDiffSum, x.GoldDiffGames, x.XpDiffSum, x.XpDiffGames))));
    }

    /// <summary>
    /// Games a matchup needs to earn a place on the leaderboard: the larger of the
    /// absolute floor and the champion's own volume times the play-rate floor.
    ///
    /// <para>
    /// The larger, not either alone. The absolute floor is the one that matters on a
    /// champion nobody plays, where a share of a small total is a fraction of a game;
    /// the share is the one that matters on a popular champion, where ten games is
    /// 0.07% of the sample and every such line outranks the real matchups. Rounding
    /// is up, so the share floor is never softened to the game below it.
    /// </para>
    /// </summary>
    private int LeaderboardFloor(long totalGames)
    {
        var settings = championsOptions.Value;
        var shareFloor = settings.MinMatchupPlayRate <= 0d
            ? 0
            : (int)Math.Ceiling(settings.MinMatchupPlayRate * totalGames);

        return Math.Max(settings.MinMatchupGames, shareFloor);
    }

    private async Task<List<ChampionMatchupEntry>> ComputeLiveAsync(
        int championId,
        string position,
        string? normalizedPatch,
        Guid riotAccountId,
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

        // A deliberate opponent lookup shows the head-to-head from a single game up;
        // the player's own leaderboard keeps the lower per-player floor. The
        // share-based floor never applies here — one player's whole matchup history
        // is smaller than one opponent's slice of the population's, so a share of it
        // rounds to a game or two and would only restate the floor above it.
        var minGames = opponentChampionId is not null
            ? 1
            : championsOptions.Value.MinPlayerMatchupGames;

        // The champion side of the lane: rows for this champion at this
        // position, on the configured queue (matched via the correlated
        // EXISTS over matches), narrowed to the one account asked for.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId && p1.TeamPosition == position)
            .Where(p1 => p1.RiotAccountId == riotAccountId)
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null
                    || EF.Functions.Like(m.GameVersion, patchPrefix!))));

        // Narrow to the requested elo bands (null = every band).
        if (bands is not null)
        {
            championRows = championRows.Where(p1 => bands.Contains(p1.EloBracket));
        }

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
                (p1, p2) => new { Opponent = p2.ChampionId, p1.Win });

        // The player's whole field on this champion and lane, counted rather than
        // summed from the rows below: those are narrowed to one opponent on a search
        // (dividing the row by itself and calling it 100%) and already floored on the
        // leaderboard (inflating every share by the dropped tail). One indexed COUNT,
        // no rows materialised — the same second read the aggregate path pays (#1098),
        // which the search here used to skip by reporting a play rate of 0.
        var totalGames = await opponentPairs.LongCountAsync(ct);

        // One SQL round-trip for the rows themselves: narrow to the requested
        // opponent, group by the opponent champion, and COUNT(*) / SUM(win) per
        // opponent. The minimum-games floor is applied in SQL (HAVING) so thin
        // samples never cross the wire.
        var rows = await opponentPairs
            .Where(x => opponentChampionId == null || x.Opponent == opponentChampionId)
            .GroupBy(x => x.Opponent)
            .Select(g => new
            {
                Opponent = g.Key,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        // Lane counters stay null. The aggregate covers every tracked account, so
        // lending them to one player's row would report the population's lane as
        // theirs; unknown is the honest answer until the fold is player-aware.
        return ToOrderedEntries(rows.Select(x => (
            x.Opponent, x.Games, x.Wins, TotalGames: totalGames, LaneOutcome: (LaneOutcome?)null)));
    }

    /// <summary>
    /// Shared final projection for both read paths: materialised
    /// (opponent, games, wins) rows — every one already above its floor, so
    /// games is never zero — mapped to entries ordered best-winrate first.
    /// The best / worst *slicing* is the caller's, and reads the Wilson bounds
    /// rather than this order.
    ///
    /// <para>
    /// The opponent id breaks ties. Win rates collide constantly at these sample
    /// sizes (every 1-of-2 matchup is 50%), and without a total order the same
    /// request can hand back the same rows in a different sequence — which reads as
    /// a data change, exactly as <c>ChampionDominantLaneFilter</c> argues.
    /// </para>
    /// </summary>
    private List<ChampionMatchupEntry> ToOrderedEntries(
        IEnumerable<(int Opponent, int Games, int Wins, long TotalGames, LaneOutcome? LaneOutcome)> rows)
    {
        var minDecidedLanes = championsOptions.Value.MinDecidedLaneGames;

        return rows
            .Select(x =>
            {
                var (lower, upper) = RateMath.WilsonInterval(x.Wins, x.Games);
                return new ChampionMatchupEntry
                {
                    OpponentChampionId = x.Opponent,
                    Games = x.Games,
                    Wins = x.Wins,
                    WinRate = RateMath.Rate(x.Wins, x.Games),
                    PlayRate = RateMath.Rate(x.Games, x.TotalGames),
                    WinRateLowerBound = lower,
                    WinRateUpperBound = upper,
                    // Decided lanes only, and only enough of them. Too few yields null
                    // rather than a rate: "no lane was ever settled here", "one lane was
                    // settled and we won it" and "the lane is always lost" are three
                    // different facts and must not render alike.
                    DecidedLaneGames = x.LaneOutcome?.Decided ?? 0,
                    LaneWinRate = x.LaneOutcome is { } outcome && outcome.Decided >= Math.Max(1, minDecidedLanes)
                        ? RateMath.Rate(outcome.Wins, outcome.Decided)
                        : null,
                    // Averaged over the lanes the gap was measured on — never over decided
                    // lanes or games, both of which are larger and would drag the average
                    // toward zero by the share of lanes nobody ever measured (#976).
                    GoldDiffLaneGames = x.LaneOutcome?.GoldDiffGames ?? 0,
                    AverageGoldDiffAt15 = x.LaneOutcome is { GoldDiffGames: > 0 } gap
                        ? (double)gap.GoldDiffSum / gap.GoldDiffGames
                        : null,
                    XpDiffLaneGames = x.LaneOutcome?.XpDiffGames ?? 0,
                    AverageXpDiffAt15 = x.LaneOutcome is { XpDiffGames: > 0 } xp
                        ? (double)xp.XpDiffSum / xp.XpDiffGames
                        : null,
                };
            })
            .OrderByDescending(m => m.WinRate)
            .ThenBy(m => m.OpponentChampionId)
            .ToList();
    }

    /// <summary>
    /// Lane wins and losses past the threshold (evens are in neither), plus the summed
    /// gold and experience gaps at 15 minutes, each over its own sample — smaller than
    /// <see cref="Decided"/> on rows folded before #976 / #1111 respectively, which is
    /// the reason all three carry separate denominators.
    /// </summary>
    private readonly record struct LaneOutcome(
        int Wins, int Losses, long GoldDiffSum, int GoldDiffGames, long XpDiffSum, int XpDiffGames)
    {
        public int Decided => Wins + Losses;
    }
}
