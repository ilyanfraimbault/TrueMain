namespace TrueMain.Services.Champions;

/// <summary>
/// Assigns an OPGG-style performance tier (S / A / B / C / D) to every
/// <c>(champion, position)</c> row of a single patch's directory.
///
/// <para>
/// <b>Score.</b> Each row gets <c>score = winRate * <see cref="WinRateWeight"/>
/// + normalizedPickRate * <see cref="PickRateWeight"/></c>. WinRate already
/// lives in <c>[0, 1]</c>. PickRate is the per-lane share of TrueMain games and
/// is typically an order of magnitude smaller (a dominant pick sits around
/// 0.10, most rows well under that), so feeding it raw would let winRate swamp
/// it. Each row's pickRate is therefore min-max normalized against the largest
/// pickRate on the patch, mapping the busiest row to 1.0 before weighting. The
/// weights lean on winRate (the signal players read a tier list for) while
/// still rewarding meta presence — a 55%-winrate niche pick should not
/// outrank a 52%-winrate staple that everyone plays.
/// </para>
///
/// <para>
/// <b>Buckets.</b> Rows are ranked by score (desc) and sliced by patch-wide
/// percentile, so the tiers are always relative to the current patch rather
/// than tied to absolute winrate cutoffs that drift between metas. The split
/// is a deliberate pyramid — few S, a fat B middle:
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
/// Pure and in-memory: it post-processes the already-materialized summary
/// rows and never touches the database, so the read itself stays owned by the
/// query service. Ties on score resolve by the caller-provided input order,
/// which the summaries query has already made deterministic
/// (pickRate desc, then championId, then position).
/// </para>
/// </summary>
internal static class ChampionTierCalculator
{
    /// <summary>Weight applied to winRate (already in <c>[0, 1]</c>).</summary>
    public const double WinRateWeight = 0.85;

    /// <summary>Weight applied to the patch-normalized pickRate (in <c>[0, 1]</c>).</summary>
    public const double PickRateWeight = 0.15;

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
    /// One row's inputs for tiering. WinRate and PickRate use the same units
    /// as <see cref="ReadModels.Champions.ChampionSummaryReadModel"/> —
    /// fractions in <c>[0, 1]</c>.
    /// </summary>
    internal readonly record struct TierInput(double WinRate, double PickRate);

    /// <summary>
    /// Returns the tier letter for each input, in the same order as
    /// <paramref name="inputs"/>. A single-row patch (or any row when only one
    /// distinct rank exists) lands at the top of the ladder — <see cref="TierS"/>.
    /// </summary>
    public static IReadOnlyList<string> Assign(IReadOnlyList<TierInput> inputs)
    {
        var count = inputs.Count;
        if (count == 0)
        {
            return [];
        }

        // Normalize pickRate against the patch max so it shares winRate's
        // [0, 1] scale before weighting. When every pickRate is 0 (degenerate
        // seed / empty meta) the normalized term collapses to 0 and the score
        // is winRate-only — still a valid, monotonic ranking.
        var maxPickRate = 0.0;
        for (var i = 0; i < count; i++)
        {
            if (inputs[i].PickRate > maxPickRate)
            {
                maxPickRate = inputs[i].PickRate;
            }
        }

        // Rank rows by score desc, carrying the original index so we can scatter
        // the assigned tiers back into the caller's order. OrderBy is stable, so
        // equal scores keep the input order (already deterministic upstream).
        var ranked = Enumerable.Range(0, count)
            .OrderByDescending(i => Score(inputs[i], maxPickRate))
            .ToList();

        var tiers = new string[count];
        for (var rank = 0; rank < count; rank++)
        {
            // Percentile of this row from the top, in [0, 1). count >= 1, so the
            // divisor is never zero; rank 0 maps to 0.0 (always S-eligible).
            var percentile = (double)rank / count;
            tiers[ranked[rank]] = TierForPercentile(percentile);
        }

        return tiers;
    }

    /// <summary>
    /// Ranking score for one row against a field whose busiest pickRate is
    /// <paramref name="maxPickRate"/> — the same blend <see cref="Assign"/>
    /// sorts by. Exposed so callers that re-present the tiered rows (e.g. the
    /// tier-list meta page) can order within a tier by the exact value that
    /// placed each row in its bucket, instead of re-deriving an ad-hoc order.
    /// Pass the maximum pickRate over the same field the inputs were tiered
    /// against; <c>0</c> collapses the score to winRate-only.
    /// </summary>
    public static double ScoreOf(TierInput input, double maxPickRate) => Score(input, maxPickRate);

    private static double Score(TierInput input, double maxPickRate)
    {
        var normalizedPickRate = maxPickRate <= 0 ? 0 : input.PickRate / maxPickRate;
        return (input.WinRate * WinRateWeight) + (normalizedPickRate * PickRateWeight);
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
