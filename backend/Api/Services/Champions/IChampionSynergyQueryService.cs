using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

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
