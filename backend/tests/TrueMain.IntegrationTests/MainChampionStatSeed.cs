using Data.Entities;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The <c>main_champion_stats</c> row that puts a seeded participant inside
/// <c>Data.Aggregation.ChampionCohort</c>. Every fold on the champion page gates on it,
/// so a suite that seeds a tracked account without one is seeding a player whose games
/// no panel counts.
/// </summary>
internal static class MainChampionStatSeed
{
    public static MainChampionStat Row(
        string platformId,
        string puuid,
        int championId,
        string position = "MIDDLE",
        bool isMain = true,
        bool isActive = true)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 100,
            ChampionMatches = 60,
            PlayRate = 0.6d,
            IsMain = isMain,
            IsActive = isActive,
            IsOtp = false,
            PrimaryPosition = position,
            PositionBreakdown = [new PositionStat { Position = position, Games = 60, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow
        };
}
