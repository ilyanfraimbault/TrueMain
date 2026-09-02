using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Aggregation;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingestor.Processes;

/// <summary>
/// Incrementally pre-aggregates same-team champion co-occurrence into
/// <c>champion_synergy_stats</c>, plus the marginal win rates the synergy metric is
/// measured against into <c>champion_synergy_baseline_stats</c> (#922).
///
/// This is <see cref="ChampionMatchupLeadAggregationProcess"/>'s design, reused
/// wholesale: each match is folded exactly once (gated by
/// <see cref="Match.SynergyAggregated"/>) into additive rows via
/// <c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>, so a cycle costs what was
/// ingested since the last run rather than what is retained in total — the
/// regression #811 had to undo for matchups. Rows carry no sample floor; the read
/// side folds them to the requested patch / elo scope and floors the merged total.
/// Aged-out patches are never revisited, so their rows freeze once retention drops
/// their matches, exactly as matchup and powerspike rows do.
///
/// Two things differ from the matchup fold:
///
/// 1. <b>Four pairs per participant, not one opponent.</b> The matchup fold pairs a
///    tracked participant with the enemy in the same lane; this one pairs it with
///    each teammate in another canonical lane. Same asymmetry, though: the
///    (champion, position) side is always a tracked account and the partner side is
///    whoever was on the team, because the question being answered is "you play X —
///    who should your friends play?". The tracked side is the shared
///    <see cref="ChampionCohort"/> — a main of the champion they are playing, in a
///    game that is not a remake — since #1365; it used to be the wider "any account we
///    know", which is the cohort mismatch #1087 had already fixed for matchups while
///    this fold and the powerspike one kept counting a different population from the
///    header above them. The <b>partner</b> side stays everyone: the expected value the
///    metric subtracts is built from an ally drawn near the population mean, so
///    narrowing it would bias every synergy on the site (#922).
///
/// 2. <b>It also writes baselines.</b> Synergy is observed minus expected win rate,
///    and expected needs each champion's marginal rate. Deriving those from another
///    aggregate would compare cohorts that do not match (different queue gates,
///    different retention state), so the same fold emits them: a <c>SELF</c> row for
///    the tracked participant and an <c>ALLY</c> row for each teammate. Both sides
///    are needed separately — see <see cref="ChampionSynergyBaselineStat"/>.
///
/// There is no <see cref="Match.TimelineIngested"/> gate. The matchup process keeps
/// one for cohort continuity with the aggregate it inherited from #606; this
/// aggregate is new, needs only participant rows, and gating it would silently
/// exclude every timeline-less match from the synergy pool for no benefit.
/// </summary>
public sealed class ChampionSynergyAggregationProcess(
    ILogger<ChampionSynergyAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<SynergyAggregationOptions> options,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    public string Name => "ChampionSynergyAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var batches = 0;
        var pairRows = 0;
        var baselineRows = 0;

        while (maxPerRun == 0 || processedMatches < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // IX_matches_synergy_pending keeps this selection an index scan. It
            // covers the whole table on day one (the flag ships false everywhere)
            // and shrinks to the pending tail as the backfill drains.
            var matchIds = await db.Matches
                .AsNoTracking()
                .Where(m => m.QueueId == queueId && !m.SynergyAggregated)
                .OrderBy(m => m.Id)
                .Take(take)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (matchIds.Count == 0)
            {
                break;
            }

            var written = await ProcessBatchAsync(db, matchIds, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            pairRows += written.PairRows;
            baselineRows += written.BaselineRows;
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        logger.LogInformation(
            "Champion synergy aggregation summary: matches={Matches}, batches={Batches}, "
            + "pairRows={PairRows}, baselineRows={BaselineRows}.",
            processedMatches,
            batches,
            pairRows,
            baselineRows);

        return new SynergyAggregationSummary(processedMatches, batches, pairRows, baselineRows);
    }

    private static async Task<WrittenRows> ProcessBatchAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var patchByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion })
            .ToDictionaryAsync(m => m.Id, m => PatchVersion.Normalize(m.GameVersion), ct);

        // Who may sit on the queried side of a pairing. Every participant below is
        // loaded regardless — an off-cohort player is still somebody's partner — and
        // membership is tested per row against this set (ChampionCohort).
        var cohort = await ChampionCohort.LoadAsync(db, matchIds, ct);

        // A participant with an empty or garbage TeamPosition cannot be placed in a
        // composition, so it is excluded on both sides of the pair rather than stored
        // as a partner nobody can ask for. Same canonical set the cohort tests, so the
        // partner side and the queried side cannot drift apart.
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId)
                && ChampionCohort.CanonicalPositions.Contains(p.TeamPosition))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.EloBracket,
                p.Win))
            .ToListAsync(ct);

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var pairs = new Dictionary<SynergyKey, Accumulator>();
        var baselines = new Dictionary<BaselineKey, Accumulator>();

        foreach (var (matchId, parts) in participantsByMatch)
        {
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            foreach (var self in parts)
            {
                // The queried side, and only it: a main of this champion, in a game
                // that lasted. The ally rows below are emitted from this seat, so the
                // partner side keeps taking whoever shared the game.
                if (!cohort.Includes(matchId, self.ParticipantId))
                {
                    continue;
                }

                Add(
                    baselines,
                    new BaselineKey(self.ChampionId, self.TeamPosition, SynergyBaselineSide.Self, patch, self.EloBracket),
                    self.Win);

                foreach (var ally in parts)
                {
                    // Same team, different lane. Comparing positions rather than
                    // identities is what excludes `self` from its own partner list,
                    // and it also makes duplicate-position dirty data (the admin
                    // data-quality "duplicateChampion" case) degrade to three
                    // partners instead of pairing a player with themselves.
                    if (ally.TeamId != self.TeamId || ally.TeamPosition == self.TeamPosition)
                    {
                        continue;
                    }

                    // Both rows are keyed on the tracked player's elo band, not the
                    // ally's: the whole slice is "what a tracked player at this rank
                    // experienced", so a rank-filtered read must select the same
                    // games on the pair table and on the baseline it divides by.
                    Add(
                        pairs,
                        new SynergyKey(
                            self.ChampionId,
                            self.TeamPosition,
                            ally.ChampionId,
                            ally.TeamPosition,
                            patch,
                            self.EloBracket),
                        self.Win);

                    Add(
                        baselines,
                        new BaselineKey(ally.ChampionId, ally.TeamPosition, SynergyBaselineSide.Ally, patch, self.EloBracket),
                        self.Win);
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await UpsertPairsAsync(db, pairs, aggregatedAtUtc, ct);
        await UpsertBaselinesAsync(db, baselines, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.SynergyAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(pairs.Count, baselines.Count);
    }

    private static void Add<TKey>(Dictionary<TKey, Accumulator> target, TKey key, bool win)
        where TKey : notnull
    {
        if (!target.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator();
            target[key] = accumulator;
        }

        accumulator.Games++;
        if (win)
        {
            accumulator.Wins++;
        }
    }

    private static async Task UpsertPairsAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<SynergyKey, Accumulator> pairs,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (pairs.Count == 0)
        {
            return;
        }

        var rows = pairs.ToList();
        const string sql = """
            INSERT INTO champion_synergy_stats
                ("Id", "ChampionId", "TeamPosition", "PartnerChampionId", "PartnerPosition",
                 "Patch", "elo_bracket", "Games", "Wins", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.partner, t.partner_pos, t.patch, t.elo, t.games, t.wins, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @partners::integer[], @partnerPositions::text[],
                        @patches::text[], @elos::text[], @games::integer[], @wins::integer[])
                AS t(champ, pos, partner, partner_pos, patch, elo, games, wins)
            ON CONFLICT ("ChampionId", "TeamPosition", "PartnerChampionId", "PartnerPosition", "Patch", "elo_bracket") DO UPDATE SET
                "Games" = champion_synergy_stats."Games" + EXCLUDED."Games",
                "Wins" = champion_synergy_stats."Wins" + EXCLUDED."Wins",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.TeamPosition).ToArray()),
                new NpgsqlParameter("partners", rows.Select(r => r.Key.PartnerChampionId).ToArray()),
                new NpgsqlParameter("partnerPositions", rows.Select(r => r.Key.PartnerPosition).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("elos", rows.Select(r => r.Key.EloBracket).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                new NpgsqlParameter("wins", rows.Select(r => r.Value.Wins).ToArray())
            ],
            ct);
    }

    private static async Task UpsertBaselinesAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<BaselineKey, Accumulator> baselines,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (baselines.Count == 0)
        {
            return;
        }

        var rows = baselines.ToList();
        const string sql = """
            INSERT INTO champion_synergy_baseline_stats
                ("Id", "ChampionId", "TeamPosition", "Side", "Patch", "elo_bracket", "Games", "Wins", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.side, t.patch, t.elo, t.games, t.wins, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @sides::text[], @patches::text[],
                        @elos::text[], @games::integer[], @wins::integer[])
                AS t(champ, pos, side, patch, elo, games, wins)
            ON CONFLICT ("Side", "ChampionId", "TeamPosition", "Patch", "elo_bracket") DO UPDATE SET
                "Games" = champion_synergy_baseline_stats."Games" + EXCLUDED."Games",
                "Wins" = champion_synergy_baseline_stats."Wins" + EXCLUDED."Wins",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.TeamPosition).ToArray()),
                new NpgsqlParameter("sides", rows.Select(r => r.Key.Side).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("elos", rows.Select(r => r.Key.EloBracket).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                new NpgsqlParameter("wins", rows.Select(r => r.Value.Wins).ToArray())
            ],
            ct);
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        string EloBracket,
        bool Win);

    private readonly record struct SynergyKey(
        int ChampionId,
        string TeamPosition,
        int PartnerChampionId,
        string PartnerPosition,
        string Patch,
        string EloBracket);

    private readonly record struct BaselineKey(
        int ChampionId,
        string TeamPosition,
        string Side,
        string Patch,
        string EloBracket);

    private readonly record struct WrittenRows(int PairRows, int BaselineRows);

    private sealed class Accumulator
    {
        public int Games;
        public int Wins;
    }
}
