namespace Data.Entities;

/// <summary>
/// How many stored <c>match_participants</c> rows played a champion, per
/// <c>(platform, patch, queue, position, champion)</c> (#2132) — the "Games" column of
/// the operator champion stats (<c>GET /ops/stats/champions</c>), moved off the request
/// path. Every filter that read accepts is one of these dimensions, so any filter
/// combination is a <c>SUM</c> over a few thousand rows instead of a grouped scan of the
/// whole participant table, which hit the API's 60 s <c>statement_timeout</c> on a cold
/// cache.
///
/// <para>
/// <b>A rollup of the raw rows, not an aggregate folded once.</b> The figure it replaces
/// counted what <c>match_participants</c> holds right now: retention deleting a patch
/// takes its games away, and a lane fix moves a participant from one position to
/// another. A fold-once counter would keep both. So <c>ChampionGameCountRefreshProcess</c>
/// recomputes the whole table from the raw rows and replaces it in one transaction
/// (#2217), slices no stored match belongs to any more included — the table is a
/// snapshot of the raw corpus as of <see cref="RefreshedAtUtc"/>, never a history.
/// </para>
///
/// <para>
/// <b>Why not <c>champion_aggregate_scopes</c>.</b> Those scopes only cover tracked
/// accounts' own games; this count includes the untracked teammates and opponents of
/// the same matches, so summing the scopes would silently undercount it.
/// </para>
/// </summary>
public class ChampionGameCount
{
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>
    /// The match's stored <c>matches."Patch"</c>, or the empty string when that generated
    /// column is null (an unparseable <c>GameVersion</c>) — the key cannot hold a null, and
    /// a patch filter never asks for the empty string, which matches the "no patch" answer
    /// the live read gave such a match.
    /// </summary>
    public string Patch { get; set; } = string.Empty;

    public int QueueId { get; set; }

    public string TeamPosition { get; set; } = string.Empty;

    public int ChampionId { get; set; }

    /// <summary>Participant rows of this champion in the slice, i.e. games played.</summary>
    public long Games { get; set; }

    /// <summary>When the slice holding this row was last recomputed.</summary>
    public DateTime RefreshedAtUtc { get; set; }
}
