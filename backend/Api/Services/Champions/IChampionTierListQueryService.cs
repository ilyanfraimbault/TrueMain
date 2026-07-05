using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionTierListQueryService
{
    /// <summary>
    /// Builds the champion meta / tier-list for a single patch
    /// (<paramref name="patch"/> if non-null and canonical, otherwise the
    /// active patch), grouping <c>(champion, position)</c> rows into S/A/B/C/D
    /// tiers by a winRate + pickRate blend. Tiering is computed independently
    /// per position. When <paramref name="position"/> is non-null only that
    /// position is returned; otherwise every position's rows are tiered and
    /// merged into the same buckets. Always returns a model (possibly with no
    /// tiers) so the caller can render its own empty state.
    /// </summary>
    Task<ChampionTierListReadModel> GetTierListAsync(string? patch, string? position, CancellationToken ct);
}
