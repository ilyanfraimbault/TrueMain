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
    /// <param name="patch">Requested patch; null resolves to the active patch.</param>
    /// <param name="position">Requested Riot team position; null spans every position.</param>
    /// <param name="eloBracket">
    /// Optional elo filter (per <c>Core.Lol.Ranking.EloBracket</c>): <c>ALL</c>,
    /// a bare tier (e.g. <c>GOLD</c> — that tier only), or a <c>TIER_PLUS</c>
    /// form (e.g. <c>GOLD_PLUS</c> — that tier and above). When null or
    /// <c>ALL</c> the tiers are computed from every band; otherwise only the
    /// selected tier(s) feed the winRate / pickRate blend.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChampionTierListReadModel> GetTierListAsync(string? patch, string? position, string? eloBracket, CancellationToken ct);
}
