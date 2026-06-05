using System.Collections.Frozen;

namespace Core.Lol.Items;

/// <summary>
/// Riot item numeric ids. Names mirror the in-game item names; groupings expose
/// commonly-used sets (trinkets, support quest items, tier-two boots…).
/// </summary>
public static class LolItemIds
{
    public const int BootsOfSpeed = 1001;

    public const int Manamune = 3004;
    public const int Muramana = 3042;
    public const int TearOfTheGoddess = 3070;
    public const int ArchangelsStaff = 3003;
    public const int Seraphs = 3040;

    public static class Trinkets
    {
        public const int StealthWard = 3340;
        public const int FarsightAlteration = 3363;
        public const int OracleLens = 3364;
        public const int ScryingOrb = 3330;

        public static readonly FrozenSet<int> All = new HashSet<int>
        {
            StealthWard,
            FarsightAlteration,
            OracleLens,
            ScryingOrb
        }.ToFrozenSet();
    }

    public static class TierTwoBoots
    {
        public const int BootsOfSwiftness = 3009;
        public const int BerserkersGreaves = 3006;
        public const int IonianBootsOfLucidity = 3158;
        public const int MercurysTreads = 3111;
        public const int PlatedSteelcaps = 3047;
        public const int SorcerersShoes = 3020;
        public const int SymbioticSoles = 3013;
        public const int SynchronizedSouls = 3117;

        public static readonly FrozenSet<int> All = new HashSet<int>
        {
            BerserkersGreaves,
            PlatedSteelcaps,
            MercurysTreads,
            SorcerersShoes,
            IonianBootsOfLucidity,
            SynchronizedSouls,
            BootsOfSwiftness,
            SymbioticSoles
        }.ToFrozenSet();
    }

    public static class RequiredBuffCurrency
    {
        public const string FeatsNoxianBootPurchase = "Feats_NoxianBootPurchaseBuff";
        public const string FeatsSpecialQuestBoot = "Feats_SpecialQuestBootBuff";

        /// <summary>
        /// Riot's stable internal marker on the in-store root of the support
        /// quest chain. Verified across patches 15.10 → 16.10. Used to detect
        /// the support-quest family dynamically per patch — see
        /// <c>CommunityDragonItemMetadataProvider</c>. The IDs themselves
        /// (World Atlas, Bloodsong, etc.) are <em>not</em> hardcoded anywhere.
        /// </summary>
        public const string SupportItemPurchase = "SupportItemPurchaseBuff";
    }
}
