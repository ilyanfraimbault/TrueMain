using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionPatchDiffQueryService
{
    /// <summary>
    /// Compares a champion's aggregates on two patches at a single position,
    /// surfacing the win-rate swing plus whether the dominant first item,
    /// keystone and skill order changed between them.
    ///
    /// <paramref name="fromPatch"/> / <paramref name="toPatch"/> are the older
    /// and newer patch to compare. When either is unspecified the service
    /// resolves a sensible default from the patches that actually have data for
    /// this <c>(champion, position)</c>: <paramref name="toPatch"/> defaults to
    /// the latest such patch and <paramref name="fromPatch"/> to the one before
    /// it, so the page opens on the most recent patch-over-patch change. The
    /// position is the requested one when given and canonical, otherwise the
    /// champion's dominant lane on its latest patch — matching the build and
    /// trend endpoints.
    ///
    /// Always returns a non-null model so the caller can render its own
    /// "not enough data" state without a 404: a patch with no data for the
    /// champion yields a null side, and the delta is null unless both sides are
    /// present.
    /// </summary>
    Task<ChampionPatchDiffReadModel> GetDiffAsync(
        int championId,
        string? fromPatch,
        string? toPatch,
        string? position,
        CancellationToken ct);
}
