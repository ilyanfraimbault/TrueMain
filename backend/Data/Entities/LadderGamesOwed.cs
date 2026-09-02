namespace Data.Entities;

/// <summary>
/// How many ranked games an account has played since its matches were last ingested (#1360):
/// the ladder's game count minus the value that count held at the last ingest.
/// </summary>
/// <remarks>
/// The rule lives here because two callers apply it and they must not drift: the match claim
/// orders by it, and the match-ids request is sized by it. The claim necessarily restates it
/// as an EF expression tree — a query cannot call this method — so any change here has to be
/// mirrored in <c>RiotAccountRepository.SelectClaimableAsync</c>, which says so at the call
/// site.
/// </remarks>
public static class LadderGamesOwed
{
    /// <summary>
    /// The games owed, or zero when the answer is not known.
    /// </summary>
    /// <remarks>
    /// Two cases deliberately yield zero rather than a number.
    /// <para>
    /// <b>No baseline.</b> An account ingested before the baseline column existed, or never
    /// ingested at all, has no "value at the last visit" to subtract. Treating the missing
    /// baseline as zero would read the player's <em>entire season</em> as owed, which after a
    /// deploy is every tracked account at once — the ordering would sort by career volume
    /// instead of by recent activity until each account had been visited once.
    /// </para>
    /// <para>
    /// <b>A negative difference.</b> A Riot season reset restarts wins and losses from the
    /// bottom, so the subtraction goes negative for every account simultaneously. Floored, such
    /// an account simply owes nothing and keeps its place in the age ordering.
    /// </para>
    /// </remarks>
    public static int From(int? ladderGames, int? ladderGamesAtLastIngest)
        => ladderGames is { } games && ladderGamesAtLastIngest is { } baseline
            ? Math.Max(0, games - baseline)
            : 0;
}
