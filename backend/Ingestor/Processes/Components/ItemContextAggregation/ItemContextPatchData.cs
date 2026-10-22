using Data;
using Data.BuildFacts;
using Data.ItemContext;
using Data.Statics;
using Ingestor.Options;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// The per-patch reference data one item-context run folds against — item metadata, the
/// champion profile snapshot and the completed-item catalogue (#1750) — each loaded once per
/// patch however many batches touch it.
/// </summary>
public sealed class ItemContextPatchData(
    IItemMetadataProvider itemMetadataProvider,
    IItemStatsProvider itemStatsProvider,
    ItemContextAggregationOptions settings)
{
    private readonly Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>> _metadata = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChampionProfileSnapshot> _profiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuiltItemCatalog> _catalogs = new(StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<int, ItemMetadata>> MetadataAsync(string patch, string gameVersion, CancellationToken ct)
    {
        if (!_metadata.TryGetValue(patch, out var metadata))
        {
            metadata = await itemMetadataProvider.GetItemsAsync(gameVersion, ct);
            _metadata[patch] = metadata;
        }

        return metadata;
    }

    public async Task<ChampionProfileSnapshot> ProfilesAsync(TrueMainDbContext db, string patch, CancellationToken ct)
    {
        if (!_profiles.TryGetValue(patch, out var snapshot))
        {
            snapshot = await ChampionProfileSnapshot.LoadAsync(
                db, patch, settings.ProfileLookbackPatches, settings.MinProfileGames, ct);
            _profiles[patch] = snapshot;
        }

        return snapshot;
    }

    /// <summary>
    /// The patch's completed items with their stats. A Data Dragon failure fails the run, as a
    /// CommunityDragon one does: folding through it would flag the batch's matches with their
    /// in-game axes missing for good.
    /// </summary>
    public async Task<BuiltItemCatalog> CatalogAsync(string patch, string gameVersion, CancellationToken ct)
    {
        if (!_catalogs.TryGetValue(patch, out var catalog))
        {
            var metadata = await MetadataAsync(patch, gameVersion, ct);
            var stats = await itemStatsProvider.GetItemsAsync(gameVersion, ct);
            catalog = BuiltItemCatalog.From(metadata, stats);
            _catalogs[patch] = catalog;
        }

        return catalog;
    }
}
