using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Synergies;

public interface IChampionSynergyQueryService
{
    /// <summary>
    /// Lists the teammates a champion performs best with at a lane, ranked by
    /// synergy (observed minus expected win rate) rather than by raw pair win
    /// rate, from the pre-aggregated <c>champion_synergy_stats</c> table.
    /// </summary>
    /// <param name="championId">Riot champion id the pairing is measured from.</param>
    /// <param name="position">
    /// Canonical Riot team position (<c>TOP</c> / <c>JUNGLE</c> / <c>MIDDLE</c> /
    /// <c>BOTTOM</c> / <c>UTILITY</c>) the champion is played at. Required and
    /// already validated by the caller.
    /// </param>
    /// <param name="patch">
    /// Requested patch (<c>major.minor</c> or full Riot version); null spans every
    /// patch the aggregate holds. Applied identically to the pair rows and to the
    /// baselines they are measured against, so both always describe one cohort.
    /// </param>
    /// <param name="partnerPosition">
    /// Optional narrowing to a single partner lane. Filters the returned pairs only
    /// — the cohort reference point stays the whole scope, so narrowing the list
    /// never moves the numbers already in it.
    /// </param>
    /// <param name="eloBracket">
    /// Optional elo filter — an exact tier (<c>GOLD</c>) or a cumulative "X+"
    /// threshold (<c>GOLD_PLUS</c>); null / <c>ALL</c> spans every band. Selects on
    /// the tracked player's rank, the same side the aggregate is keyed on.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The partner list ordered by synergy descending, with the champion's own
    /// baseline and the cohort reference point attached. Empty (still a 200) when
    /// no pair clears the games floor, or when the champion's own sample is too
    /// thin for an expected win rate to mean anything.
    /// </returns>
    Task<ChampionSynergiesResponse> GetSynergiesAsync(
        int championId,
        string position,
        string? patch,
        string? partnerPosition,
        string? eloBracket,
        CancellationToken ct);

    /// <summary>
    /// Extends a chosen duo to a trio: for the games where this champion and this
    /// partner played together, the third teammates that over- or under-performed
    /// what the three marginals predicted. Computed live from
    /// <c>match_participants</c> — the triple space is far too sparse to
    /// pre-aggregate — and therefore scoped to the retention window.
    /// </summary>
    /// <param name="championId">Riot champion id of the queried champion.</param>
    /// <param name="position">Canonical Riot team position of the queried champion.</param>
    /// <param name="partnerChampionId">Riot champion id of the already-chosen partner.</param>
    /// <param name="partnerPosition">
    /// Canonical Riot team position of that partner. Must differ from
    /// <paramref name="position"/>; an identical value simply matches nothing,
    /// since one team cannot field two players in a lane.
    /// </param>
    /// <param name="patch">
    /// Requested patch (<c>major.minor</c> or full Riot version); null spans every
    /// patch still inside the retention window.
    /// </param>
    /// <param name="eloBracket">
    /// Optional elo filter, applied to the queried champion's side exactly as in
    /// <see cref="GetSynergiesAsync"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The duo's own game count and win rate plus the qualifying third picks,
    /// ordered by synergy descending. An empty completion list is the normal
    /// answer for a rarely-played duo, not an error.
    /// </returns>
    Task<ChampionTrioSynergiesResponse> GetTrioSynergiesAsync(
        int championId,
        string position,
        int partnerChampionId,
        string partnerPosition,
        string? patch,
        string? eloBracket,
        CancellationToken ct);
}

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
    IOptions<ChampionsListOptions> championsOptions,
    IChampionReadCache cache)
    : IChampionSynergyQueryService
{
    public Task<ChampionSynergiesResponse> GetSynergiesAsync(
        int championId,
        string position,
        string? patch,
        string? partnerPosition,
        string? eloBracket,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            $"champions:synergies:{championId}:{position}:{PatchFilter.Normalize(patch) ?? "all"}"
                + $":{partnerPosition ?? "any"}:{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeSynergiesAsync(championId, position, patch, partnerPosition, eloBracket, token),
            ct);

    private async Task<ChampionSynergiesResponse> ComputeSynergiesAsync(
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
            ChampionWinRate = self.WinRate,
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

        var selfWinRate = self.WinRate;
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

            var allyWinRate = ally.WinRate;
            var observed = RateMath.Rate(row.Wins, row.Games);
            var expected = baselines.ExpectedWinRate(selfWinRate, [allyWinRate]);

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

    public Task<ChampionTrioSynergiesResponse> GetTrioSynergiesAsync(
        int championId,
        string position,
        int partnerChampionId,
        string partnerPosition,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
        // The trio fold is a live three-way self-join over match_participants and had
        // no cache of its own before #1368.
        => cache.GetOrComputeAsync(
            $"champions:synergies-trio:{championId}:{position}:{partnerChampionId}:{partnerPosition}"
                + $":{PatchFilter.Normalize(patch) ?? "all"}:{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeTrioSynergiesAsync(
                championId, position, partnerChampionId, partnerPosition, patch, eloBracket, token),
            ct);

    private async Task<ChampionTrioSynergiesResponse> ComputeTrioSynergiesAsync(
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
                    // The folds' lane set: a third teammate on a garbage
                    // TeamPosition is not offered as a trio completion.
                    && ChampionCohort.CanonicalPositions.Contains(p3.TeamPosition)),
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

        var selfWinRate = self.WinRate;
        var partnerWinRate = partnerBaseline.WinRate;
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

            var thirdWinRate = third.WinRate;
            var observed = RateMath.Rate(row.Wins, row.Games);
            var expected = baselines.ExpectedWinRate(selfWinRate, [partnerWinRate, thirdWinRate]);

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
    private async Task<SynergyBaselineSet> ReadBaselinesAsync(
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
            .Select(g => new SynergyBaselineRow(
                g.Key.Side,
                g.Key.ChampionId,
                g.Key.TeamPosition,
                g.Sum(x => x.Games),
                g.Sum(x => x.Wins)))
            .ToListAsync(ct);

        return SynergyBaselineSet.From(rows);
    }
}
