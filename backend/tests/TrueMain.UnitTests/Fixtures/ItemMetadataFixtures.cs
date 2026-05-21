using Core.Lol.Items;
using Ingestor.Processes.Components.PatternAggregation;

namespace TrueMain.UnitTests.Fixtures;

internal static class ItemMetadataFixtures
{
    // Raw item IDs are allowed in this test fixture — production code must
    // never carry hardcoded IDs (the support-quest detection pipeline derives
    // everything from CommunityDragon metadata at runtime). The IDs below
    // model a patch-16.10 World-Atlas chain: 3865 root → 3866/3867
    // intermediates → 3877 completion.
    public static readonly IReadOnlyDictionary<int, ItemMetadata> ItemMetadataById = new Dictionary<int, ItemMetadata>
    {
        [LolItemIds.Trinkets.StealthWard] = new(LolItemIds.Trinkets.StealthWard, 0, true, false, false, false, true, false),
        [2003] = new(2003, 50, true, true, false, false, true, false),
        [LolItemIds.Cull] = new(LolItemIds.Cull, 450, true, false, false, false, true, false),
        [1055] = new(1055, 450, true, false, false, false, false, false),
        [1056] = new(1056, 400, true, false, false, false, false, false),
        [LolItemIds.BootsOfSpeed] = new(LolItemIds.BootsOfSpeed, 300, true, false, true, true, false, false),
        [LolItemIds.TearOfTheGoddess] = new(LolItemIds.TearOfTheGoddess, 400, true, false, false, false, false, false),
        // 3865 = World Atlas (root starter of the support-quest chain)
        [3865] = new(3865, 400, true, false, false, false, false, false)
        {
            IsSupportQuestStarter = true
        },
        // 3866 = Runic Compass (first transitional, not in store)
        [3866] = new(3866, 400, false, false, false, false, false, false)
        {
            IsSupportQuestIntermediate = true
        },
        // 3867 = Bounty of Worlds (second transitional, not in store)
        [3867] = new(3867, 400, false, false, false, false, false, false)
        {
            IsSupportQuestIntermediate = true
        },
        // 3877 = Bloodsong (one of the 5 in-store leaves)
        [3877] = new(3877, 400, true, false, false, false, true, false)
        {
            IsSupportQuestCompletion = true
        },
        // 3899 — synthetic: a support-quest root that *would* be eligible as
        // a final build item if not for IsSupportQuestStarter. Doesn't model
        // any real item — exists so FinalBuildResolver tests can validate
        // that the support-quest filter (and not IsFinalItem=false or the
        // starter-items filter) is what drops the root from the build path.
        [3899] = new(3899, 400, true, false, false, false, true, false)
        {
            IsSupportQuestStarter = true
        },
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
        [6672] = new(6672, 3000, true, false, false, false, true, false)
    };
}
