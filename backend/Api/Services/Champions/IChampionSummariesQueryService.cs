using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionSummariesQueryService
{
    /// <summary>
    /// Lightweight directory query: one <see cref="ChampionSummaryReadModel"/>
    /// per <c>(champion, position)</c> pair on the active queue, all rows
    /// pinned to a single patch (<paramref name="patch"/> if non-null and
    /// canonical, otherwise the global latest patch in the aggregate table),
    /// wrapped with the resolved patch and true total games (#972 — see
    /// <see cref="ChampionSummariesResult"/>). Used by the champions list /
    /// index page and the homepage overview; callers that need builds, runes
    /// or patterns go through <c>GET /champions/{id}</c>.
    ///
    /// <paramref name="eloBracket"/> is a cumulative "X+" threshold (see
    /// <see cref="Core.Lol.Ranking.EloBracket"/>); null / ALL spans every band.
    /// </summary>
    Task<ChampionSummariesResult> GetAllSummariesAsync(
        string? patch, string? eloBracket, CancellationToken ct);

    /// <summary>
    /// Volume counters for the served patch and the patches before it, newest first,
    /// at most <paramref name="patchCount"/> of them (#1109). Never reaches past the
    /// served patch: a patch the servable bar rejected is not one the homepage counts
    /// either.
    ///
    /// <para>
    /// Deliberately not <see cref="GetAllSummariesAsync"/> once per patch — the
    /// homepage needs two numbers, not two directories, and this takes one grouped
    /// scan shared with the patch resolution itself. Returns fewer entries than asked
    /// (possibly none) when the window reaches past the patches that exist.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ChampionPatchVolume>> GetServedPatchVolumesAsync(
        int patchCount, CancellationToken ct);
}
