namespace Core.Lol.Identifiers;

// Explicit, stable values so reordering the members never shifts a number.
// Routing maps by name (RiotRouting) and PlatformId persists the string, so the
// numbers aren't relied on today — pinning them keeps it safe if they ever are
// (EF column, JSON). 0 is left unused so default(PlatformRoute) is invalid.
public enum PlatformRoute
{
    BR1 = 1,
    EUN1 = 2,
    EUW1 = 3,
    JP1 = 4,
    KR = 5,
    LA1 = 6,
    LA2 = 7,
    NA1 = 8,
    OC1 = 9,
    PH2 = 10,
    RU = 11,
    SG2 = 12,
    TH2 = 13,
    TR1 = 14,
    TW2 = 15,
    VN2 = 16
}
