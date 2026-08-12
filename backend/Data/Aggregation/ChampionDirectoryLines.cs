namespace Data.Aggregation;

/// <summary>
/// What a <c>(champion, lane)</c> <b>line</b> is on a patch — the unit the public
/// directory renders, the unit the admin's patch-coverage page counts, and now the
/// unit the serving decision itself rests on (#1109).
///
/// <para>
/// Three consumers have to agree on it. <c>ChampionSummariesQueryService</c> renders
/// the lines that clear the floor, <c>PatchCoverageQueryService</c> reports how many
/// clear it, and <c>ChampionAggregateScopeResolver</c> decides whether a patch has
/// enough of them to be served at all. If the count that gates serving ever drifts
/// from the count that renders, the site switches onto a patch its own directory
/// then declares empty — which is the exact failure #1109 exists to remove, so the
/// definition lives here rather than three times over.
/// </para>
///
/// <para>
/// Neither rule is guessable from the schema. A <c>champion_aggregate_scopes</c> row
/// is per (account, champion, patch, platform, queue, lane, elo), so one line is the
/// sum of many rows — on production a settled patch folds ~50 000 rows into ~560
/// lines. And a blank <c>Position</c> is the "no lane" sentinel (the column is
/// non-nullable), carrying nothing a lane-relative page can rank: the directory drops
/// it, so it is not coverage either.
/// </para>
/// </summary>
public static class ChampionDirectoryLines
{
    /// <summary>
    /// Whether a scope row carries a lane at all. Trimmed rather than compared
    /// against <c>""</c>: the no-lane sentinel is a non-nullable column that has been
    /// written both empty and blank over the life of the table, and a whitespace row
    /// counted as a lane is a line the directory will never show.
    /// </summary>
    public static bool CarriesLane(string position) => position.Trim().Length > 0;

    /// <summary>
    /// Whether a line's sample is large enough for the directory to rank it —
    /// <c>ChampionsList:MinSampleGames</c>. A floor of 0 or less keeps every line.
    /// </summary>
    public static bool ClearsFloor(ChampionDirectoryLine line, int floor) => line.Games >= floor;

    /// <summary>
    /// Folds per-scope game sums into one line per <c>(patch, champion, lane)</c>,
    /// dropping the lane-less rows. Callers hand over rows already grouped in SQL;
    /// this collapses whatever grouping remains — including two <c>GameVersion</c>
    /// forms that normalise onto the same patch, which are one patch and not two.
    /// </summary>
    public static IReadOnlyList<ChampionDirectoryLine> Fold(IEnumerable<ChampionDirectoryLine> rows)
        => [.. rows
            .Where(row => CarriesLane(row.Position))
            .GroupBy(row => (row.Patch, row.ChampionId, row.Position))
            .Select(group => new ChampionDirectoryLine(
                group.Key.Patch,
                group.Key.ChampionId,
                group.Key.Position,
                group.Sum(row => row.Games)))];
}

/// <summary>
/// One <c>(champion, lane)</c> line on a patch and the games behind it. Also used as
/// the input shape of <see cref="ChampionDirectoryLines.Fold"/>, where
/// <see cref="Games"/> is a partial sum rather than the line's total.
/// </summary>
public readonly record struct ChampionDirectoryLine(string Patch, int ChampionId, string Position, long Games);
