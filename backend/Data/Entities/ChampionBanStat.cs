namespace Data.Entities;

/// <summary>
/// How many retained matches banned a champion, per patch and elo band (#920) —
/// the numerator of the ban rate. Its denominator lives in
/// <see cref="BanScopeTotal"/> on exactly the same grain minus the champion, and
/// the two are written by the same fold from the same matches so a rate is always
/// computed over one cohort.
///
/// <para>
/// <b>Why a separate aggregate rather than a column on champion_aggregate_scopes.</b>
/// Those scopes are per tracked account and only cover a truemain's own games, so
/// their pick rate is a main-population share. Bans come from every ingested match
/// regardless of who was tracked in it, so a ban count folded into a scope row
/// would be a different population under the same roof. Keeping the two apart is
/// also why pick + ban "presence" is deliberately not offered: the two rates have
/// different denominators and adding them would produce a number with no meaning.
/// </para>
///
/// <para>
/// <b>No history before the feature shipped.</b> Raw match payloads are not kept,
/// so bans could not be backfilled; <c>AddMatchBans</c> flags every pre-existing
/// match as already folded precisely so those ban-less matches never enter the
/// denominator and deflate every rate. Reads must therefore treat a missing scope
/// as "unknown", not as zero.
/// </para>
/// </summary>
public class ChampionBanStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// Elo band this count is scoped to — a per-tier <c>EloBracket</c> value, or the
    /// synthetic <c>ALL</c>. Unlike every other aggregate, <c>ALL</c> is stored here
    /// rather than derived by summing the bands: a match is counted once per distinct
    /// band among its tracked participants, so a match spanning two bands appears in
    /// both and the bands cannot be summed back into an unfiltered total.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>
    /// Matches in this scope where the champion was banned, counted once per match
    /// however many times it was banned in it.
    /// </summary>
    public int Bans { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
