using Core.Lol.Ranking;
using Core.Lol.Synergy;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Champion synergies query (#922) — the duo slice and its on-demand trio
/// extension.
///
/// Duos are served from the pre-aggregated <c>champion_synergy_stats</c> table:
/// one indexed read on the (champion, position) prefix, folded to the requested
/// patch / elo scope with the games floor applied on the merged total, exactly as
/// <see cref="ChampionMatchupQueryService"/> reads matchups. Trios are computed
/// live, because the triple space is too sparse to be worth storing — see
/// <see cref="ChampionTrioSynergiesResponse"/>.
///
/// Both paths score with the same model. The metric is observed minus expected
/// win rate, expected being built from marginals read out of
/// <c>champion_synergy_baseline_stats</c>: the champion's own rate as a tracked
/// player, each ally's rate as somebody's teammate, and the cohort rate that
/// anchors them. Every scope filter is applied to the baselines as well as to the
/// pairs, so a rank-filtered or patch-filtered answer never compares a slice
/// against a population it was not drawn from.
/// </summary>
public sealed class ChampionSynergyQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions)
    : IChampionSynergyQueryService
{
    // The five canonical lane positions — the same set the aggregation folds, so a
    // third teammate on a garbage TeamPosition is not offered as a trio completion.
    private static readonly string[] CanonicalPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public async Task<ChampionSynergiesResponse> GetSynergiesAsync(
        int championId,
        string position,
        string? patch,
        string? partnerPosition,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = PatchFilter.Normalize(patch);
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var settings = championsOptions.Value;
        var minBaselineGames = settings.MinSynergyBaselineGames;

        var baselines = await ReadBaselinesAsync(normalizedPatch, bands, ct);
        var self = baselines.Self(championId, position);

        // The pairing floor scales with how much the champion is played, exactly as
        // the matchup one does (#1087): an absolute floor alone let 21-game pairings
        // — 0.26% of the champion's games — top the ranking, which is worse here than
        // on matchups because synergy is a difference of two rates and carries the
        // sum of their error. The larger of the two floors applies, so a rarely
        // played champion still falls back to the absolute one.
        var shareFloor = settings.MinSynergyPlayRate <= 0d
            ? 0
            : (int)Math.Ceiling(settings.MinSynergyPlayRate * self.Games);
        var minGames = Math.Max(settings.MinSynergyGames, shareFloor);

        var response = new ChampionSynergiesResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            PartnerPosition = partnerPosition,
            MinGames = minGames,
            ChampionGames = self.Games,
            ChampionWinRate = RateMath.Rate(self.Wins, self.Games),
            CohortWinRate = baselines.CohortWinRate,
        };

        // Without a usable baseline for the champion itself — or for the cohort it is
        // compared against — every expected win rate below would be invented. Return
        // the (real) sample sizes and no entries, so the caller can say why rather
        // than print a number nobody can stand behind.
        if (baselines.CohortGames == 0 || self.Games < minBaselineGames)
        {
            return response;
        }

        var query = db.ChampionSynergyStats
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
        if (partnerPosition is not null)
        {
            query = query.Where(s => s.PartnerPosition == partnerPosition);
        }

        // Rows are stored per (partner, partner lane, patch, band) with no floor.
        // Fold to the requested scope, then floor the merged total so the
        // all-patches view floors on the real total rather than on one slice.
        var rows = await query
            .GroupBy(s => new { s.PartnerChampionId, s.PartnerPosition })
            .Select(g => new
            {
                g.Key.PartnerChampionId,
                g.Key.PartnerPosition,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        var selfWinRate = RateMath.Rate(self.Wins, self.Games);
        var partners = new List<ChampionSynergyEntry>(rows.Count);

        foreach (var row in rows)
        {
            var ally = baselines.Ally(row.PartnerChampionId, row.PartnerPosition);
            if (ally.Games < minBaselineGames)
            {
                continue;
            }

            // Is this lane a role the partner actually plays? A pairing can clear
            // every games floor and still be a role-detection artefact — "Sylas
            // BOTTOM" led this list on production — and no reader can act on a duo
            // whose other half does not exist.
            if (!baselines.IsRealLane(
                row.PartnerChampionId, row.PartnerPosition, settings.MinSynergyPartnerLanePlayRate))
            {
                continue;
            }

            var allyWinRate = RateMath.Rate(ally.Wins, ally.Games);
            var observed = RateMath.Rate(row.Wins, row.Games);
            var expected = SynergyMath.ExpectedWinRate(selfWinRate, [allyWinRate], baselines.CohortWinRate);

            partners.Add(new ChampionSynergyEntry
            {
                PartnerChampionId = row.PartnerChampionId,
                PartnerPosition = row.PartnerPosition,
                Games = row.Games,
                Wins = row.Wins,
                WinRate = observed,
                PlayRate = RateMath.Rate(row.Games, self.Games),
                PartnerBaselineGames = ally.Games,
                PartnerBaselineWinRate = allyWinRate,
                ExpectedWinRate = expected,
                Synergy = observed - expected,
            });
        }

        return response with
        {
            // (champion, lane) breaks the ties: synergy is a difference of two rates
            // and collides freely at these sample sizes, and a list that reshuffles
            // between two identical requests reads as a data change — the reasoning
            // ChampionDominantLaneFilter already spells out.
            Partners = partners
                .OrderByDescending(p => p.Synergy)
                .ThenBy(p => p.PartnerChampionId)
                .ThenBy(p => p.PartnerPosition, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public async Task<ChampionTrioSynergiesResponse> GetTrioSynergiesAsync(
        int championId,
        string position,
        int partnerChampionId,
        string partnerPosition,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = PatchFilter.Normalize(patch);
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var minGames = championsOptions.Value.MinSynergyTrioGames;
        var minBaselineGames = championsOptions.Value.MinSynergyBaselineGames;

        // Same queue cast as the sibling champion reads, so the trio slice is drawn
        // from the same population as the duo aggregate it extends.
        var queueId = (int)options.Value.QueueId;

        // The champion side: tracked rows for this champion at this lane, on the
        // configured queue and patch, optionally narrowed to a set of elo bands.
        // IX_match_participants_champion_position_tracked serves this seek.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId && p1.TeamPosition == position && p1.RiotAccountId != null)
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null || m.Patch == normalizedPatch)));

        if (bands is not null)
        {
            championRows = championRows.Where(p1 => bands.Contains(p1.EloBracket));
        }

        // Narrow to the games the duo actually shared before touching the third
        // dimension: this is what keeps the query bounded by the pair's game count
        // (tens to hundreds) instead of the champion's (tens of thousands), which
        // matters because Postgres runs these single-threaded here.
        var pairRows = championRows.Where(p1 => db.MatchParticipants.Any(p2 =>
            p2.MatchId == p1.MatchId
            && p2.TeamId == p1.TeamId
            && p2.ChampionId == partnerChampionId
            && p2.TeamPosition == partnerPosition));

        var pair = await pairRows
            .GroupBy(_ => 1)
            .Select(g => new { Games = g.Count(), Wins = g.Sum(x => x.Win ? 1 : 0) })
            .FirstOrDefaultAsync(ct);

        var pairGames = pair?.Games ?? 0;
        var pairWins = pair?.Wins ?? 0;

        var response = new ChampionTrioSynergiesResponse
        {
            ChampionId = championId,
            Position = position,
            PartnerChampionId = partnerChampionId,
            PartnerPosition = partnerPosition,
            Patch = normalizedPatch,
            MinGames = minGames,
            PairGames = pairGames,
            PairWins = pairWins,
            PairWinRate = RateMath.Rate(pairWins, pairGames),
        };

        // A duo that never cleared the trio floor on its own cannot have a third pick
        // that does, so skip both the join and the baseline read.
        if (pairGames < minGames)
        {
            return response;
        }

        var rows = await pairRows
            .SelectMany(
                p1 => db.MatchParticipants.Where(p3 =>
                    p3.MatchId == p1.MatchId
                    && p3.TeamId == p1.TeamId
                    && p3.TeamPosition != p1.TeamPosition
                    && p3.TeamPosition != partnerPosition
                    && CanonicalPositions.Contains(p3.TeamPosition)),
                (p1, p3) => new { p3.ChampionId, p3.TeamPosition, p1.Win })
            .GroupBy(x => new { x.ChampionId, x.TeamPosition })
            .Select(g => new
            {
                g.Key.ChampionId,
                g.Key.TeamPosition,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return response;
        }

        var baselines = await ReadBaselinesAsync(normalizedPatch, bands, ct);
        var self = baselines.Self(championId, position);
        var partnerBaseline = baselines.Ally(partnerChampionId, partnerPosition);

        if (baselines.CohortGames == 0
            || self.Games < minBaselineGames
            || partnerBaseline.Games < minBaselineGames)
        {
            return response;
        }

        var selfWinRate = RateMath.Rate(self.Wins, self.Games);
        var partnerWinRate = RateMath.Rate(partnerBaseline.Wins, partnerBaseline.Games);
        var completions = new List<ChampionTrioSynergyEntry>(rows.Count);

        foreach (var row in rows)
        {
            var third = baselines.Ally(row.ChampionId, row.TeamPosition);
            if (third.Games < minBaselineGames)
            {
                continue;
            }

            // Same role check as the duo path — a third pick nobody plays at that
            // lane is no more actionable than a partner nobody plays there. No share
            // floor here though: a trio's sample is a subset of its duo's, and a
            // share of the pair's games would leave almost every duo with no third
            // pick at all, which is the reason MinSynergyTrioGames already sits below
            // MinSynergyGames.
            if (!baselines.IsRealLane(
                row.ChampionId, row.TeamPosition, championsOptions.Value.MinSynergyPartnerLanePlayRate))
            {
                continue;
            }

            var thirdWinRate = RateMath.Rate(third.Wins, third.Games);
            var observed = RateMath.Rate(row.Wins, row.Games);
            var expected = SynergyMath.ExpectedWinRate(
                selfWinRate,
                [partnerWinRate, thirdWinRate],
                baselines.CohortWinRate);

            completions.Add(new ChampionTrioSynergyEntry
            {
                ChampionId = row.ChampionId,
                Position = row.TeamPosition,
                Games = row.Games,
                Wins = row.Wins,
                WinRate = observed,
                BaselineGames = third.Games,
                BaselineWinRate = thirdWinRate,
                ExpectedWinRate = expected,
                Synergy = observed - expected,
            });
        }

        return response with
        {
            // Same total order as the duo list above, for the same reason.
            Completions = completions
                .OrderByDescending(c => c.Synergy)
                .ThenBy(c => c.ChampionId)
                .ThenBy(c => c.Position, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>
    /// Loads every marginal win rate in the requested scope in one round trip.
    /// Reading the whole scope rather than the specific champions asked for is
    /// deliberate: the table is bounded by (champion × lane × side × patch × band),
    /// so a scope holds low thousands of pre-aggregated rows, and one grouped scan
    /// beats building a large IN list — which the trio path would otherwise need
    /// twice, once for the pair and once for every candidate third pick.
    /// </summary>
    private async Task<BaselineSet> ReadBaselinesAsync(
        string? normalizedPatch,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        var query = db.ChampionSynergyBaselineStats.AsNoTracking();
        if (normalizedPatch is not null)
        {
            query = query.Where(b => b.Patch == normalizedPatch);
        }
        if (bands is not null)
        {
            query = query.Where(b => bands.Contains(b.EloBracket));
        }

        var rows = await query
            .GroupBy(b => new { b.Side, b.ChampionId, b.TeamPosition })
            .Select(g => new BaselineRow(
                g.Key.Side,
                g.Key.ChampionId,
                g.Key.TeamPosition,
                g.Sum(x => x.Games),
                g.Sum(x => x.Wins)))
            .ToListAsync(ct);

        return BaselineSet.From(rows);
    }

    private sealed record BaselineRow(string Side, int ChampionId, string TeamPosition, int Games, int Wins);

    /// <summary>
    /// The marginals for one scope, indexed for lookup.
    /// <see cref="CohortGames"/> / <see cref="CohortWins"/> sum the <c>SELF</c> side,
    /// which is exactly one row per tracked participant per folded match — so the
    /// cohort rate is the tracked population's overall win rate and never
    /// double-counts a game the way summing the four-per-participant <c>ALLY</c> side
    /// would.
    /// </summary>
    private sealed class BaselineSet
    {
        private readonly Dictionary<(int ChampionId, string Position), Marginal> _self;
        private readonly Dictionary<(int ChampionId, string Position), Marginal> _ally;

        /// <summary>Ally games per champion summed over every lane — <see cref="IsRealLane"/>'s denominator.</summary>
        private readonly Dictionary<int, int> _laneTotals;

        private BaselineSet(
            Dictionary<(int, string), Marginal> self,
            Dictionary<(int, string), Marginal> ally,
            Dictionary<int, int> laneTotals,
            int cohortGames,
            int cohortWins)
        {
            _self = self;
            _ally = ally;
            _laneTotals = laneTotals;
            CohortGames = cohortGames;
            CohortWins = cohortWins;
        }

        public int CohortGames { get; }

        public int CohortWins { get; }

        public double CohortWinRate => RateMath.Rate(CohortWins, CohortGames);

        public static BaselineSet From(IReadOnlyList<BaselineRow> rows)
        {
            var self = new Dictionary<(int, string), Marginal>();
            var ally = new Dictionary<(int, string), Marginal>();
            var laneTotals = new Dictionary<int, int>();
            var cohortGames = 0;
            var cohortWins = 0;

            foreach (var row in rows)
            {
                if (string.Equals(row.Side, SynergyBaselineSide.Self, StringComparison.Ordinal))
                {
                    self[(row.ChampionId, row.TeamPosition)] = new Marginal(row.Games, row.Wins);
                    cohortGames += row.Games;
                    cohortWins += row.Wins;
                }
                else if (string.Equals(row.Side, SynergyBaselineSide.Ally, StringComparison.Ordinal))
                {
                    ally[(row.ChampionId, row.TeamPosition)] = new Marginal(row.Games, row.Wins);
                    laneTotals[row.ChampionId] = laneTotals.GetValueOrDefault(row.ChampionId, 0) + row.Games;
                }
            }

            return new BaselineSet(self, ally, laneTotals, cohortGames, cohortWins);
        }

        /// <summary>The champion's own marginal, or an empty one when it has no games in scope.</summary>
        public Marginal Self(int championId, string position)
            => _self.GetValueOrDefault((championId, position), Marginal.Empty);

        /// <summary>The champion's marginal as somebody's teammate, or an empty one.</summary>
        public Marginal Ally(int championId, string position)
            => _ally.GetValueOrDefault((championId, position), Marginal.Empty);

        /// <summary>
        /// Whether <paramref name="position"/> is a lane this champion actually plays:
        /// its share of the champion's ally games across every lane, against
        /// <paramref name="minLanePlayRate"/>. A champion with no ally games at all in
        /// scope fails — nothing is known about its roles, and the caller's other
        /// floors have already established the pairing is thin.
        ///
        /// <para>
        /// The denominator is the whole <c>ALLY</c> side for that champion, which is
        /// why this lives on the baseline set rather than being derived from the
        /// pairing rows: those are already filtered to one champion's teammates and to
        /// lanes other than its own, so a share computed from them would measure the
        /// wrong thing — Udyr would read as a 100% toplaner on a jungler's page purely
        /// because his jungle games cannot appear there.
        /// </para>
        /// </summary>
        public bool IsRealLane(int championId, string position, double minLanePlayRate)
        {
            if (minLanePlayRate <= 0d)
            {
                return true;
            }

            var lane = Ally(championId, position).Games;
            if (lane == 0)
            {
                return false;
            }

            var acrossLanes = _laneTotals.GetValueOrDefault(championId, 0);
            return acrossLanes > 0 && (double)lane / acrossLanes >= minLanePlayRate;
        }
    }

    private readonly record struct Marginal(int Games, int Wins)
    {
        public static Marginal Empty { get; } = new(0, 0);
    }
}
