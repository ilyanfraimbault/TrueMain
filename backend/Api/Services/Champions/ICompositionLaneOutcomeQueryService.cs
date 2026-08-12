using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface ICompositionLaneOutcomeQueryService
{
    /// <summary>
    /// Judges the lane in the games a recommendation was computed from (#1117) — the
    /// same games every other cell of the matchup tool's stat line counts.
    /// </summary>
    /// <param name="position">
    /// Canonical Riot team position both lane sides share. Already validated upstream.
    /// </param>
    /// <param name="matches">
    /// The selection's (match, participant) keys, in any order. Empty yields
    /// <see cref="CompositionLaneReadModel.Empty"/> without touching the database.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Counters over the subset of those games whose lane could be judged at all —
    /// both sides needing a 15-minute snapshot — which is why the measured count is
    /// returned rather than left to be assumed equal to the sample size.
    /// </returns>
    Task<CompositionLaneReadModel> GetAsync(
        string position,
        IReadOnlyList<CompositionMatchRef> matches,
        CancellationToken ct);
}
