namespace DevSeed;

/// <summary>
/// Real Riot ids for items/spells/runes per role archetype, and one seed row per
/// (champion, position) — deliberately the same data shape and the same ids as
/// <c>web/server/utils/dev-api-mock.ts</c> so the two dev mocks (web JSON layer,
/// backend Postgres layer) describe a recognizably similar world. Not read from
/// any external source: kept as a small, deterministic, hand-picked slice so the
/// tool has no network dependency.
/// </summary>
public sealed record Archetype(
    int[] StarterItems,
    int[] Boots,
    int[] Items,
    (int Spell1, int Spell2) Spells,
    string[][] SkillOrders);

public static class Spells
{
    public const int Flash = 4, Ignite = 14, Teleport = 12, Heal = 7, Exhaust = 3, Barrier = 21, Smite = 11, Ghost = 6;
}

public static class ChampionArchetypes
{
    public static readonly IReadOnlyDictionary<string, Archetype> Archetypes = new Dictionary<string, Archetype>
    {
        ["marksman"] = new(
            [1055, 2003], [3006, 3047], [6672, 3031, 3094, 3036, 3072, 3085, 3153, 3046],
            (Spells.Flash, Spells.Heal), [["Q", "W", "E"], ["Q", "E", "W"]]),
        ["mage"] = new(
            [1056, 2003], [3020, 3158], [6655, 4645, 3089, 3157, 3135, 3116, 3102, 4628],
            (Spells.Flash, Spells.Ignite), [["Q", "W", "E"], ["Q", "E", "W"]]),
        ["fighter"] = new(
            [1054, 2003], [3047, 3111], [3078, 3071, 3053, 6333, 3161, 3074, 3026, 3748],
            (Spells.Flash, Spells.Teleport), [["Q", "E", "W"], ["Q", "W", "E"]]),
        ["assassin"] = new(
            [1055, 2003], [3158, 3047], [6692, 6676, 3142, 6697, 3814, 6695, 3156, 3036],
            (Spells.Flash, Spells.Ignite), [["Q", "W", "E"], ["W", "Q", "E"]]),
        ["tank"] = new(
            [1054, 2003], [3047, 3111], [3068, 3065, 3075, 3110, 3083, 3742, 6665, 3084],
            (Spells.Flash, Spells.Teleport), [["Q", "W", "E"], ["W", "Q", "E"]]),
        ["enchanter"] = new(
            [3865, 2003], [3158, 3111], [6617, 3107, 3222, 3504, 2065, 3190, 3011, 3050],
            (Spells.Flash, Spells.Ignite), [["E", "Q", "W"], ["Q", "E", "W"]]),
        ["jungler"] = new(
            [1102, 2003], [3047, 3158], [6631, 3071, 3053, 6333, 3074, 3161, 3026, 3814],
            (Spells.Flash, Spells.Smite), [["Q", "E", "W"], ["W", "Q", "E"]]),
    };

    /// <summary>[keystones, row1, row2, row3] per rune style id.</summary>
    public static readonly IReadOnlyDictionary<int, int[][]> StylePerks = new Dictionary<int, int[][]>
    {
        [8000] = [[8005, 8008, 8010, 8021], [9111, 8009, 9101], [9104, 9105, 9103], [8014, 8017, 8299]],
        [8100] = [[8112, 8128, 9923], [8126, 8139, 8143], [8136, 8120, 8138], [8135, 8105, 8106]],
        [8200] = [[8214, 8229, 8230], [8226, 8275, 8224], [8210, 8234, 8233], [8237, 8232, 8236]],
        [8300] = [[8351, 8360, 8369], [8306, 8304, 8321], [8313, 8352, 8345], [8347, 8410, 8316]],
        [8400] = [[8437, 8439, 8465], [8446, 8463, 8401], [8429, 8444, 8473], [8451, 8453, 8242]],
    };

    public static readonly int[] StatOffense = [5008, 5005, 5007];
    public static readonly int[] StatFlex = [5008, 5010, 5001];
    public static readonly int[] StatDefense = [5011, 5013, 5001];

    public static readonly IReadOnlyList<ChampionSeed> Seeds =
    [
        // TOP
        new(266, "TOP", "fighter", 8010, 8000, 8400, 0.512, 0.081),
        new(122, "TOP", "fighter", 8010, 8000, 8400, 0.523, 0.094),
        new(114, "TOP", "fighter", 8010, 8000, 8100, 0.507, 0.066),
        new(86, "TOP", "fighter", 8010, 8000, 8400, 0.531, 0.087),
        new(24, "TOP", "fighter", 8010, 8000, 8300, 0.516, 0.052),
        new(54, "TOP", "tank", 8437, 8400, 8300, 0.527, 0.043),
        new(516, "TOP", "tank", 8437, 8400, 8300, 0.503, 0.038),
        new(17, "TOP", "mage", 8128, 8100, 8200, 0.495, 0.041),
        new(23, "TOP", "fighter", 8008, 8000, 8200, 0.509, 0.029),
        new(106, "TOP", "fighter", 8230, 8200, 8400, 0.514, 0.031),
        // JUNGLE
        new(64, "JUNGLE", "jungler", 8010, 8000, 8100, 0.489, 0.083),
        new(104, "JUNGLE", "jungler", 8008, 8000, 8100, 0.506, 0.061),
        new(121, "JUNGLE", "assassin", 8112, 8100, 8000, 0.521, 0.057),
        new(245, "JUNGLE", "assassin", 8128, 8100, 8200, 0.502, 0.049),
        new(11, "JUNGLE", "jungler", 8008, 8000, 8100, 0.528, 0.062),
        new(234, "JUNGLE", "jungler", 8010, 8000, 8100, 0.497, 0.078),
        new(32, "JUNGLE", "tank", 8439, 8400, 8300, 0.536, 0.042),
        new(154, "JUNGLE", "tank", 8439, 8400, 8300, 0.524, 0.038),
        new(76, "JUNGLE", "mage", 8112, 8100, 8200, 0.481, 0.024),
        new(56, "JUNGLE", "jungler", 8128, 8100, 8000, 0.526, 0.045),
        // MIDDLE
        new(103, "MIDDLE", "mage", 8112, 8100, 8200, 0.522, 0.089),
        new(134, "MIDDLE", "mage", 8112, 8100, 8300, 0.517, 0.064),
        new(157, "MIDDLE", "fighter", 8008, 8000, 8100, 0.484, 0.096),
        new(777, "MIDDLE", "fighter", 8008, 8000, 8100, 0.498, 0.088),
        new(238, "MIDDLE", "assassin", 8112, 8100, 8200, 0.503, 0.074),
        new(91, "MIDDLE", "assassin", 8112, 8100, 8000, 0.515, 0.043),
        new(268, "MIDDLE", "mage", 8229, 8200, 8000, 0.476, 0.037),
        new(4, "MIDDLE", "mage", 8360, 8300, 8200, 0.511, 0.033),
        new(8, "MIDDLE", "mage", 8230, 8200, 8100, 0.520, 0.031),
        new(112, "MIDDLE", "mage", 8229, 8200, 8300, 0.513, 0.041),
        // BOTTOM
        new(222, "BOTTOM", "marksman", 8008, 8000, 8100, 0.525, 0.104),
        new(145, "BOTTOM", "marksman", 8008, 8000, 8100, 0.501, 0.112),
        new(202, "BOTTOM", "marksman", 8369, 8300, 8100, 0.519, 0.083),
        new(51, "BOTTOM", "marksman", 8008, 8000, 8300, 0.507, 0.078),
        new(81, "BOTTOM", "marksman", 8369, 8300, 8200, 0.488, 0.091),
        new(119, "BOTTOM", "marksman", 8008, 8000, 8100, 0.522, 0.039),
        new(67, "BOTTOM", "marksman", 8008, 8000, 8200, 0.512, 0.057),
        new(22, "BOTTOM", "marksman", 8008, 8000, 8200, 0.516, 0.048),
        new(360, "BOTTOM", "marksman", 8010, 8000, 8100, 0.495, 0.044),
        new(29, "BOTTOM", "marksman", 8008, 8000, 8100, 0.529, 0.036),
        // UTILITY
        new(412, "UTILITY", "enchanter", 8439, 8400, 8300, 0.497, 0.086),
        new(89, "UTILITY", "tank", 8439, 8400, 8300, 0.518, 0.071),
        new(111, "UTILITY", "tank", 8439, 8400, 8100, 0.511, 0.064),
        new(117, "UTILITY", "enchanter", 8214, 8200, 8300, 0.524, 0.058),
        new(16, "UTILITY", "enchanter", 8214, 8200, 8400, 0.531, 0.043),
        new(25, "UTILITY", "enchanter", 8214, 8200, 8300, 0.506, 0.049),
        new(53, "UTILITY", "tank", 8439, 8400, 8300, 0.514, 0.047),
        new(555, "UTILITY", "assassin", 9923, 8100, 8300, 0.492, 0.053),
        new(235, "UTILITY", "marksman", 8369, 8300, 8200, 0.503, 0.046),
        new(43, "UTILITY", "enchanter", 8214, 8200, 8300, 0.509, 0.039),
    ];

    /// <summary>
    /// How much each position roams: out-of-lane KP share used to bias generated
    /// kill positions. The JUNGLE entry is unused by the current Roam read —
    /// ChampionRoamQueryService excludes junglers entirely (no meaningful own
    /// lane) — kept here only so this dictionary stays a complete position map.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> RoamSharePerPosition = new Dictionary<string, double>
    {
        ["TOP"] = 0.14,
        ["JUNGLE"] = 0.66,
        ["MIDDLE"] = 0.34,
        ["BOTTOM"] = 0.17,
        ["UTILITY"] = 0.46,
    };

    /// <summary>Win-rate slope by game length per archetype — marksmen/mages scale up, assassins/junglers peak early.</summary>
    public static readonly IReadOnlyDictionary<string, double> ScalingSlope = new Dictionary<string, double>
    {
        ["marksman"] = 0.05,
        ["mage"] = 0.03,
        ["fighter"] = 0.005,
        ["assassin"] = -0.045,
        ["tank"] = 0.015,
        ["enchanter"] = 0.02,
        ["jungler"] = -0.03,
    };
}

public sealed record ChampionSeed(
    int Id,
    string Position,
    string ArchetypeKey,
    int Keystone,
    int PrimaryStyle,
    int SecondaryStyle,
    double WinRate,
    double PickRate);
