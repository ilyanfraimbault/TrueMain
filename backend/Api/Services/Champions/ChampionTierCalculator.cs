using TrueMain.Options;

namespace TrueMain.Services.Champions;

/// <summary>
/// Assigns an OPGG-style performance tier (S / A / B / C / D) to every
/// <c>(champion, position)</c> row of a single patch's directory, and the
/// blended score that ranks rows within a tier.
///
/// <para>
/// <b>Score (#971 — presence first).</b> A tier list is read primarily as
/// "what does the population actually play and ban", not as a pure win-rate
/// leaderboard — a noisy win rate is easy for a handful of games to swing,
/// while pick rate and ban rate are population-scale signals that a single
/// game barely moves. The score therefore weights pick rate highest, ban
/// rate second, and a <i>shrunk</i> win rate third
/// (see <see cref="ChampionTierOptions"/> for the exact weights, default
/// 45% / 30% / 25%).
/// </para>
///
/// <para>
/// <b>Win-rate shrinkage.</b> Before scoring, each row's win rate is pulled
/// toward the field's overall win rate in proportion to how few games it
/// rests on: <c>wrAdj = (wins + K * prior) / (games + K)</c>. A row with
/// far fewer than <c>K</c> games (see
/// <see cref="ChampionTierOptions.WinRateShrinkageGames"/>) lands close to
/// the prior; a well-sampled staple is barely moved. This is what keeps a
/// 12-game 70%-WR fluke out of S-tier — the weight rebalance alone isn't
/// enough, since a raw 70% vs. 53% gap is still large before any weighting.
/// </para>
///
/// <para>
/// <b>Percentile normalization, per lane.</b> Each of the three metrics is
/// converted to its rank-percentile <i>within the same <see cref="TierInput.Position"/></i>
/// before blending, rather than min-max normalized against a single
/// patch-wide maximum. Two reasons: percentile rank is insensitive to a
/// single outlier row crushing every other row's normalized value toward 0
/// (the failure mode of min-max), and normalizing within the lane corrects
/// for lane size — UTILITY has far fewer playable champions than MIDDLE, so
/// a support's raw pick rate is mechanically higher than a mid laner's for
/// the same "share of the meta", and the two are not comparable un-normalized.
/// </para>
///
/// <para>
/// <b>Missing ban data.</b> Patches before ban ingestion (#920) carry a null
/// ban rate on every row. When every row in a call has a null ban rate, the
/// ban term is dropped and its weight is folded back into pick rate and win
/// rate, proportionally to their own weights — never a fabricated 0%. Mixed
/// null/non-null within one call is not expected (ban data is populated per
/// patch, not per champion) and is treated the same as fully-null: the
/// scoring is deliberately all-or-nothing per call so a tier list is never
/// silently a different formula for a handful of rows.
/// </para>
///
/// <para>
/// <b>Buckets.</b> Rows are ranked by score (desc) and sliced by percentile
/// across the same set the score was computed over — a single lane's rows,
/// by caller convention (see the note on <see cref="Evaluate"/>) — rather
/// than tied to absolute cutoffs that drift between metas. The split is a
/// deliberate pyramid — few S, a fat B middle:
/// </para>
/// <list type="bullet">
///   <item><description>S — top 10%</description></item>
///   <item><description>A — next 20% (10–30%)</description></item>
///   <item><description>B — next 35% (30–65%)</description></item>
///   <item><description>C — next 25% (65–90%)</description></item>
///   <item><description>D — bottom 10%</description></item>
/// </list>
///
/// <para>
/// Pure and in-memory: it post-processes already-materialized summary rows
/// and never touches the database. Ties on score resolve by the
/// caller-provided input order, which the summaries query has already made
/// deterministic (pickRate desc, then championId, then position).
/// </para>
/// </summary>
internal static class ChampionTierCalculator
{
    // Cumulative share of rows, from the top, at which each tier ends. A row's
    // 0-based rank / total places it on this ladder: the first 10% are S, up
    // to 30% are A, and so on. Kept as upper bounds (exclusive at the top end,
    // except D which is the catch-all remainder) so the buckets tile [0, 1).
    private const double STierMaxPercentile = 0.10;
    private const double ATierMaxPercentile = 0.30;
    private const double BTierMaxPercentile = 0.65;
    private const double CTierMaxPercentile = 0.90;

    public const string TierS = "S";
    public const string TierA = "A";
    public const string TierB = "B";
    public const string TierC = "C";
    public const string TierD = "D";

    /// <summary>
    /// S &gt; A &gt; B &gt; C &gt; D — the display order every caller that groups or
    /// sorts by tier letter should use, so a sparse field's emitted groups
    /// (or a teaser's rows) are always strongest-first regardless of which
    /// letters actually occur. Shared here so <c>ChampionTierListQueryService</c>
    /// and <c>ChampionOverviewQueryService</c> can't drift from each other.
    /// </summary>
    public static readonly string[] TierOrder = [TierS, TierA, TierB, TierC, TierD];

    /// <summary>
    /// One row's inputs for tiering. <see cref="Position"/> scopes the
    /// pick/ban/win percentile ranks — every metric is normalized only
    /// against other rows sharing the same position. Games/Wins (rather than
    /// a precomputed win rate) are required so the calculator can apply
    /// sample-size shrinkage itself.
    /// </summary>
    internal readonly record struct TierInput(
        string Position, int Games, int Wins, double PickRate, double? BanRate);

    /// <summary>One row's tier letter and the blended score that placed it there.</summary>
    internal readonly record struct TierResult(string Tier, double Score);

    /// <summary>
    /// Returns the tier + score for each input, in the same order as
    /// <paramref name="inputs"/>. A single-row set (or any row when only one
    /// distinct rank exists) lands at the top of the ladder — <see cref="TierS"/>.
    ///
    /// <para>
    /// <paramref name="inputs"/> should be a single lane's rows — every
    /// percentile rank (see <see cref="PercentileRanksByPosition"/>) and the
    /// shrinkage prior (see <see cref="ShrinkWinRate"/>) are both computed
    /// across the whole set passed in, and the S/A/B/C/D bucket cutoff is a
    /// plain <c>rank / count</c> over that same set. Mixing multiple lanes
    /// into one call would let a thin lane trivially top its own tiny peer
    /// group on every metric and out-rank a much larger, genuinely competitive
    /// lane — both <see cref="Services.Champions.ChampionSummariesQueryService"/>
    /// and <see cref="ChampionTierListQueryService"/> call this once per
    /// position for exactly this reason, so a row's <c>TierScore</c> is
    /// expected to match between <c>GET /champions</c> and
    /// <c>GET /champions/tierlist</c> for the same <c>(patch, eloBracket,
    /// position)</c> — see <see cref="ReadModels.Champions.ChampionSummaryReadModel.TierScore"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TierResult> Evaluate(
        IReadOnlyList<TierInput> inputs, ChampionTierOptions options)
    {
        var count = inputs.Count;
        if (count == 0)
        {
            return [];
        }

        // Field-wide prior for win-rate shrinkage: the aggregate win rate
        // across every row being tiered together in this call (not per-lane —
        // see the "not expected to match bit for bit" note on Evaluate's own
        // doc above). A degenerate all-zero-games field (shouldn't happen —
        // every row here already cleared the directory's MinSampleGames floor)
        // falls back to 0.5 so shrinkage still produces a defined value.
        var totalGames = 0L;
        var totalWins = 0L;
        for (var i = 0; i < count; i++)
        {
            totalGames += inputs[i].Games;
            totalWins += inputs[i].Wins;
        }
        var prior = totalGames > 0 ? (double)totalWins / totalGames : 0.5;

        var shrunkWinRates = new double[count];
        for (var i = 0; i < count; i++)
        {
            shrunkWinRates[i] = ShrinkWinRate(inputs[i], prior, options.WinRateShrinkageGames);
        }

        // Ban data is scored only when every row carries one — see the class
        // doc's "Missing ban data" section for why this is all-or-nothing
        // rather than per-row.
        var hasBanData = true;
        for (var i = 0; i < count; i++)
        {
            if (inputs[i].BanRate is null)
            {
                hasBanData = false;
                break;
            }
        }

        var (pickWeight, banWeight, winWeight) = ResolveWeights(options, hasBanData);

        var pickPercentiles = PercentileRanksByPosition(inputs, i => inputs[i].PickRate);
        var winPercentiles = PercentileRanksByPosition(inputs, i => shrunkWinRates[i]);
        var banPercentiles = hasBanData
            ? PercentileRanksByPosition(inputs, i => inputs[i].BanRate!.Value)
            : null;

        var scores = new double[count];
        for (var i = 0; i < count; i++)
        {
            var score = (pickPercentiles[i] * pickWeight) + (winPercentiles[i] * winWeight);
            if (banPercentiles is not null)
            {
                score += banPercentiles[i] * banWeight;
            }
            scores[i] = score;
        }

        // Rank rows by score desc, carrying the original index so the assigned
        // tiers can be scattered back into the caller's order. OrderBy is
        // stable, so equal scores keep the input order (already deterministic
        // upstream).
        var ranked = Enumerable.Range(0, count)
            .OrderByDescending(i => scores[i])
            .ToList();

        var results = new TierResult[count];
        for (var rank = 0; rank < count; rank++)
        {
            // Percentile of this row from the top, in [0, 1). count >= 1, so the
            // divisor is never zero; rank 0 maps to 0.0 (always S-eligible).
            var percentile = (double)rank / count;
            var originalIndex = ranked[rank];
            results[originalIndex] = new TierResult(TierForPercentile(percentile), scores[originalIndex]);
        }

        return results;
    }

    /// <summary>
    /// <c>wrAdj = (wins + K * prior) / (games + K)</c>. <c>K = 0</c> disables
    /// shrinkage entirely (the raw win rate is returned unchanged).
    /// </summary>
    private static double ShrinkWinRate(TierInput input, double prior, int shrinkageGames)
    {
        if (shrinkageGames <= 0)
        {
            return input.Games > 0 ? (double)input.Wins / input.Games : prior;
        }

        return (input.Wins + (shrinkageGames * prior)) / (input.Games + shrinkageGames);
    }

    /// <summary>
    /// Ban weight is dropped (and its share folded proportionally into pick +
    /// win) when the field has no ban data at all — see the class doc's
    /// "Missing ban data" section.
    /// </summary>
    private static (double PickWeight, double BanWeight, double WinWeight) ResolveWeights(
        ChampionTierOptions options, bool hasBanData)
    {
        if (hasBanData)
        {
            return (options.PickRateWeight, options.BanRateWeight, options.WinRateWeight);
        }

        var remaining = options.PickRateWeight + options.WinRateWeight;
        if (remaining <= 0)
        {
            // Degenerate configuration (only BanRateWeight is non-zero) with no
            // ban data available: fall back to an even split so the score is
            // still defined rather than collapsing to 0 for every row.
            return (0.5, 0, 0.5);
        }

        return (options.PickRateWeight / remaining, 0, options.WinRateWeight / remaining);
    }

    /// <summary>
    /// For every input index, its rank-percentile among the other inputs
    /// sharing the same <see cref="TierInput.Position"/>, in <c>[0, 1]</c> —
    /// <c>0</c> is the lane's lowest value, <c>1</c> its highest. Ties share
    /// the same percentile (average-rank convention), and a lane of size 1
    /// always scores <c>1</c> (nothing to rank against). <paramref name="valueOf"/>
    /// reads the metric being ranked off the original input index so the same
    /// helper serves pick rate, win rate and ban rate without re-grouping.
    /// </summary>
    private static double[] PercentileRanksByPosition(
        IReadOnlyList<TierInput> inputs, Func<int, double> valueOf)
    {
        var count = inputs.Count;
        var result = new double[count];

        foreach (var group in Enumerable.Range(0, count).GroupBy(i => inputs[i].Position, StringComparer.Ordinal))
        {
            var indices = group.ToList();
            if (indices.Count == 1)
            {
                result[indices[0]] = 1.0;
                continue;
            }

            // Sort ascending by value, then assign each distinct value the
            // average of the 0-based ranks it occupies (ties share a
            // percentile instead of an arbitrary tie-break order).
            var sorted = indices.OrderBy(valueOf).ToList();
            var denominator = sorted.Count - 1;

            var start = 0;
            while (start < sorted.Count)
            {
                var value = valueOf(sorted[start]);
                var end = start;
                while (end + 1 < sorted.Count && valueOf(sorted[end + 1]) == value)
                {
                    end++;
                }

                var averageRank = (start + end) / 2.0;
                var percentile = averageRank / denominator;
                for (var i = start; i <= end; i++)
                {
                    result[sorted[i]] = percentile;
                }

                start = end + 1;
            }
        }

        return result;
    }

    private static string TierForPercentile(double percentile) => percentile switch
    {
        < STierMaxPercentile => TierS,
        < ATierMaxPercentile => TierA,
        < BTierMaxPercentile => TierB,
        < CTierMaxPercentile => TierC,
        _ => TierD,
    };
}
