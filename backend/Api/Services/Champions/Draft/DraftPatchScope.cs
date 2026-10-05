using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Draft;

/// <summary>
/// The patches a draft answer reads: the current one, and the one before it as
/// the fallback for a reading the current patch is too thin to carry (#1906).
/// </summary>
/// <remarks>
/// Until #1906 the desktop sent no patch and every draft read summed every
/// stored patch, so a champion's matchup was the average of several balance
/// states. Reading the current patch alone would be blind on patch day, when the
/// fold is still draining, which is the reason the build reads fall back a patch
/// too (#1467). Both null means the tables hold no patch at all: no clause.
/// </remarks>
public sealed record DraftPatchScope(string? Current, string? Previous)
{
    public static readonly DraftPatchScope Every = new(null, null);

    /// <summary>The patches a query reads, or null for no patch clause.</summary>
    public IReadOnlyList<string>? Window => Current is null
        ? null
        : Previous is null ? [Current] : [Current, Previous];

    /// <summary>Stable cache-key fragment.</summary>
    public string Token => $"{Current ?? "all"}+{Previous ?? "-"}";
}

public interface IDraftPatchScopeResolver
{
    Task<DraftPatchScope> ResolveAsync(string? requestedPatch, CancellationToken ct);
}

/// <summary>
/// Resolves <see cref="DraftPatchScope"/> from the patches the draft tables
/// actually hold.
/// </summary>
/// <remarks>
/// The list comes from <c>champion_synergy_baseline_stats</c>, the smallest table
/// every draft read shares (lane priors, opponent shares, synergy baselines), and
/// is cached on the aggregation version like every champion read: it only grows
/// when a patch's first fold lands.
/// </remarks>
public sealed class DraftPatchScopeResolver(TrueMainDbContext db, IChampionReadCache cache)
    : IDraftPatchScopeResolver
{
    public async Task<DraftPatchScope> ResolveAsync(string? requestedPatch, CancellationToken ct)
    {
        var patches = await cache.GetOrComputeAsync("champions:draft:patches", LoadNewestFirstAsync, ct);
        return Resolve(PatchFilter.Normalize(requestedPatch), patches);
    }

    /// <summary>
    /// A requested patch and the newest stored one before it; without a request,
    /// the two newest stored patches.
    /// </summary>
    internal static DraftPatchScope Resolve(string? requested, IReadOnlyList<string> newestFirst)
    {
        if (requested is null)
        {
            return newestFirst.Count switch
            {
                0 => DraftPatchScope.Every,
                1 => new DraftPatchScope(newestFirst[0], null),
                _ => new DraftPatchScope(newestFirst[0], newestFirst[1]),
            };
        }

        if (!PatchVersion.TryParse(requested, out var current))
        {
            return DraftPatchScope.Every;
        }

        var previous = newestFirst.FirstOrDefault(patch =>
            PatchVersion.TryParse(patch, out var version) && version < current);
        return new DraftPatchScope(requested, previous);
    }

    private async Task<IReadOnlyList<string>> LoadNewestFirstAsync(CancellationToken ct)
    {
        var patches = await db.ChampionSynergyBaselineStats
            .AsNoTracking()
            .Select(b => b.Patch)
            .Distinct()
            .ToListAsync(ct);

        return patches
            .Select(patch => (patch, ok: PatchVersion.TryParse(patch, out var version), version))
            .Where(entry => entry.ok)
            .OrderByDescending(entry => entry.version)
            .Select(entry => entry.patch)
            .ToList();
    }
}
