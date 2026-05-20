namespace Ingestor.Processes.Components.PatternAggregation;

public sealed record ItemMetadata(
    int Id,
    int PriceTotal,
    bool InStore,
    bool IsConsumable,
    bool IsBootsItem,
    bool IsBaseBoots,
    bool IsFinalItem,
    bool IsFinalBoots)
{
    public bool IsInventoryTransformItem { get; init; }
    public int? TransformFromItemId { get; init; }
    public bool IsSupportQuestCompletion { get; init; }
}
