using Core.Lol.Patches;
using Data;
using Data.Entities;
using Data.ItemContext;
using Data.Statics;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Profiles;

public interface IChampionDamageProfileQueryService
{
    Task<ChampionDamageProfilesResponse> GetAsync(string? patch, CancellationToken ct);
}

/// <summary>
/// Every champion's measured damage profile (#1905) — physical / magic / true shares, the
/// per-game weight a team bar needs, and the split by build archetype — for the desktop
/// draft's team damage bars.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fold's snapshot, exactly.</b> Profiles are resolved through
/// <see cref="ChampionProfileSnapshotRules"/>, the rule the item-context fold and the
/// next-item read use: same lookback, same games floor, same best-covered-position fallback.
/// A bar the player sees and the axis that drives the build advice cannot disagree.
/// </para>
/// <para>
/// <b>Order of preference</b>: measured at the position → measured at the champion's
/// best-covered position (the champion-wide entry) → a static Data Dragon class with no
/// shares → nothing. A fallback never carries a percentage: Data Dragon's ratings are
/// hand-authored and old, good for a class, not for a number.
/// </para>
/// <para>
/// <b>Cache.</b> One entry per requested patch through <see cref="IChampionReadCache"/>:
/// keyed by the aggregation version, so it lives until the ingestor publishes new numbers,
/// bounded by the read cache's 30-minute backstop. The payload is every champion of a
/// patch, hence its size.
/// </para>
/// </remarks>
public sealed class ChampionDamageProfileQueryService(
    TrueMainDbContext db,
    IChampionReadCache cache,
    IChampionStaticsProvider statics,
    ILogger<ChampionDamageProfileQueryService> logger) : IChampionDamageProfileQueryService
{
    public Task<ChampionDamageProfilesResponse> GetAsync(string? patch, CancellationToken ct)
    {
        var requestedPatch = PatchFilter.Normalize(patch);

        return cache.GetOrComputeAsync(
            $"champions:damage-profiles:{requestedPatch ?? "auto"}",
            token => LoadAsync(requestedPatch, token),
            ct,
            size: 16);
    }

    private async Task<ChampionDamageProfilesResponse> LoadAsync(string? requestedPatch, CancellationToken ct)
    {
        var patch = requestedPatch ?? await NewestProfiledPatchAsync(ct);
        if (patch is null)
        {
            return new ChampionDamageProfilesResponse();
        }

        var snapshot = await ChampionProfileSnapshotRules.LoadAsync(db, patch, ct);
        var buildRows = await LoadBuildRowsAsync(snapshot, ct);

        var profiles = new List<ChampionDamageProfileReadModel>();
        AddMeasured(profiles, snapshot.ByPosition, buildRows, perPosition: true);
        AddMeasured(profiles, snapshot.ByChampion, buildRows, perPosition: false);

        var measured = profiles.Select(profile => profile.ChampionId).ToHashSet();
        profiles.AddRange(await FallbacksAsync(patch, measured, ct));

        return new ChampionDamageProfilesResponse { Patch = patch, Profiles = profiles };
    }

    private static void AddMeasured(
        List<ChampionDamageProfileReadModel> profiles,
        IEnumerable<ChampionProfileFacts> facts,
        IReadOnlyDictionary<(int, string, string), List<ChampionDamageProfileStat>> buildRows,
        bool perPosition)
    {
        foreach (var entry in facts.OrderBy(entry => entry.ChampionId).ThenBy(entry => entry.Position, StringComparer.Ordinal))
        {
            var rows = buildRows.GetValueOrDefault((entry.ChampionId, entry.Position, entry.Patch)) ?? [];
            var profile = DamageProfileClassifier.Measured(entry, perPosition ? entry.Position : null, rows);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }
    }

    /// <summary>The newest patch with a profile above the floor — what the snapshot can resolve at all.</summary>
    private async Task<string?> NewestProfiledPatchAsync(CancellationToken ct)
    {
        var patches = await db.ChampionProfileStats
            .AsNoTracking()
            .Where(profile => profile.Games >= ChampionProfileSnapshotRules.MinGames)
            .Select(profile => profile.Patch)
            .Distinct()
            .ToListAsync(ct);

        return patches
            .Select(raw => PatchVersion.TryParse(raw, out var version) ? (Raw: raw, Version: version) : default)
            .Where(entry => entry.Raw is not null)
            .OrderByDescending(entry => entry.Version)
            .Select(entry => entry.Raw)
            .FirstOrDefault();
    }

    /// <summary>The archetype rows of exactly the profile rows the snapshot resolved, grouped by their grain.</summary>
    private async Task<IReadOnlyDictionary<(int, string, string), List<ChampionDamageProfileStat>>> LoadBuildRowsAsync(
        ChampionProfileSnapshot snapshot,
        CancellationToken ct)
    {
        var patches = snapshot.ByPosition.Concat(snapshot.ByChampion)
            .Select(facts => facts.Patch)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (patches.Count == 0)
        {
            return new Dictionary<(int, string, string), List<ChampionDamageProfileStat>>();
        }

        var rows = await db.ChampionDamageProfileStats
            .AsNoTracking()
            .Where(row => patches.Contains(row.Patch))
            .ToListAsync(ct);

        return rows
            .GroupBy(row => (row.ChampionId, row.Position, row.Patch))
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    /// <summary>
    /// The static class of every champion Data Dragon knows and the snapshot does not. An
    /// unreachable Data Dragon costs the fallbacks, never the measured profiles.
    /// </summary>
    private async Task<IEnumerable<ChampionDamageProfileReadModel>> FallbacksAsync(
        string patch,
        IReadOnlySet<int> measured,
        CancellationToken ct)
    {
        IReadOnlyDictionary<int, ChampionStatics> champions;
        try
        {
            champions = await statics.GetChampionsAsync(patch, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Champion statics unavailable for patch {Patch}; the damage profiles are served without fallbacks.", patch);
            return [];
        }

        return champions.Values
            .Where(champion => !measured.Contains(champion.ChampionId))
            .Select(champion => (champion.ChampionId, Class: DamageProfileClassifier.FallbackClass(champion)))
            .Where(entry => entry.Class is not null)
            .OrderBy(entry => entry.ChampionId)
            .Select(entry => new ChampionDamageProfileReadModel
            {
                ChampionId = entry.ChampionId,
                Source = ChampionDamageProfileSources.Fallback,
                DamageClass = entry.Class,
            })
            .ToList();
    }
}
