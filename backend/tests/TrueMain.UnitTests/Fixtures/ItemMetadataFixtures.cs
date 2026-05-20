using Core.Lol.Items;
using Ingestor.Processes.Components.PatternAggregation;

namespace TrueMain.UnitTests.Fixtures;

internal static class ItemMetadataFixtures
{
    public static readonly IReadOnlyDictionary<int, ItemMetadata> ItemMetadataById = new Dictionary<int, ItemMetadata>
    {
        [LolItemIds.Trinkets.StealthWard] = new(LolItemIds.Trinkets.StealthWard, 0, true, false, false, false, true, false),
        [2003] = new(2003, 50, true, true, false, false, true, false),
        [LolItemIds.Cull] = new(LolItemIds.Cull, 450, true, false, false, false, true, false),
        [1055] = new(1055, 450, true, false, false, false, false, false),
        [1056] = new(1056, 400, true, false, false, false, false, false),
        [LolItemIds.BootsOfSpeed] = new(LolItemIds.BootsOfSpeed, 300, true, false, true, true, false, false),
        [LolItemIds.TearOfTheGoddess] = new(LolItemIds.TearOfTheGoddess, 400, true, false, false, false, false, false),
        [LolItemIds.SupportQuest.SpellthiefsEdge] = new(LolItemIds.SupportQuest.SpellthiefsEdge, 400, true, false, false, false, false, false),
        [LolItemIds.SupportQuest.RelicShield] = new(LolItemIds.SupportQuest.RelicShield, 400, true, false, false, false, false, false),
        [LolItemIds.SupportQuest.SteelShoulderguards] = new(LolItemIds.SupportQuest.SteelShoulderguards, 400, true, false, false, false, false, false),
        [LolItemIds.TierTwoBoots.BerserkersGreaves] = new(LolItemIds.TierTwoBoots.BerserkersGreaves, 1100, true, false, true, false, true, true),
        [LolItemIds.Manamune] = new(LolItemIds.Manamune, 2900, true, false, false, false, true, false),
        [LolItemIds.Muramana] = new(LolItemIds.Muramana, 2900, false, false, false, false, true, false)
        {
            IsInventoryTransformItem = true,
            TransformFromItemId = LolItemIds.Manamune
        },
        [3031] = new(3031, 3000, true, false, false, false, true, false),
        [3085] = new(3085, 3000, true, false, false, false, true, false),
        [3153] = new(3153, 3200, true, false, false, false, true, false),
        [3877] = new(3877, 400, true, false, false, false, true, false)
        {
            IsSupportQuestCompletion = true
        },
        [3876] = new(3876, 400, true, false, false, false, true, false)
        {
            IsSupportQuestCompletion = true
        },
        [6672] = new(6672, 3000, true, false, false, false, true, false)
    };
}
