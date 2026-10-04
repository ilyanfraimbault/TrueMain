using Microsoft.EntityFrameworkCore;

namespace Data.Queries;

/// <summary>
/// Active mains per (platform, champion) — the input of the coverage deficit, spelled as SQL
/// once (#1153).
///
/// <para>
/// The ingestor's coverage snapshot (#1150) and the admin cockpit's region-balance panel
/// both read it. Were the panel to count mains its own way — every main row, say, rather than
/// only the active ones — it would show a deficit the claim allocator never used, which is
/// the one thing the panel exists to rule out.
/// </para>
/// </summary>
public static class ActiveMainCoverageQuery
{
    public static async Task<Dictionary<(string PlatformId, int ChampionId), int>> CountByPlatformAndChampionAsync(
        TrueMainDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Served by the partial (PlatformId, ChampionId) WHERE IsMain AND IsActive index.
        var rows = await db.MainChampionStats
            .AsNoTracking()
            .Where(s => s.IsMain && s.IsActive)
            .GroupBy(s => new { s.PlatformId, s.ChampionId })
            .Select(g => new { g.Key.PlatformId, g.Key.ChampionId, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(row => (row.PlatformId, row.ChampionId), row => row.Count);
    }
}
