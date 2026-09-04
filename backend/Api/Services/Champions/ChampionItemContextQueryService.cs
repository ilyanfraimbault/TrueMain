using Data;
using Data.Entities;
using Data.ItemContext;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Serves the situational build context (#1450) to the champion page and the matchup tool
/// (#1451). Deliberately the thinnest query service on the page: the fold already decided
/// every class, every finding and every rate, so this reads one range of
/// <c>champion_item_context_verdicts</c> and projects it.
/// </summary>
/// <remarks>
/// <para>
/// <b>No rank or population parameter.</b> The verdicts carry no elo dimension — a
/// situation is far rarer than a champion, and splitting eleven ways starves the buckets
/// the feature rests on — so accepting an <c>eloBracket</c> here would be a filter that
/// silently does nothing. The response says <c>allRanks</c> instead, and the card says it
/// to the reader.
/// </para>
/// <para>
/// <b>Not re-sliced by the matchup filter either.</b> A pinned <c>?vs=</c> re-slices the
/// build panels live, but a matchup answers a different question from a situation, and the
/// verdicts are not folded per opponent. The card carries the scope it was computed on
/// rather than being hidden or silently re-labelled.
/// </para>
/// </remarks>
public sealed class ChampionItemContextQueryService(
    TrueMainDbContext db,
    IChampionReadCache cache) : IChampionItemContextQueryService
{
    public Task<ChampionItemContextResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct = default)
        => cache.GetOrComputeAsync(
            $"champions:item-context:{championId}:{position}:{patch ?? "auto"}",
            token => ComputeAsync(championId, position, patch, token),
            ct);

    private async Task<ChampionItemContextResponse> ComputeAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct)
    {
        var normalizedPatch = PatchFilter.Normalize(patch);

        var scoped = db.ChampionItemContextVerdicts
            .AsNoTracking()
            .Where(verdict => verdict.ChampionId == championId && verdict.Position == position);

        // Resolving the patch costs one cheap index-only lookup on the grain index, and it
        // is what lets the page ask before its own patch filter has settled. Ordering on
        // the stored string is safe here and nowhere else: the column holds canonical
        // major.minor values written by the fold, never raw game versions.
        var servedPatch = normalizedPatch
            ?? await scoped
                .Select(verdict => verdict.Patch)
                .OrderByDescending(value => value)
                .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(servedPatch))
        {
            return new ChampionItemContextResponse
            {
                ChampionId = championId,
                Position = position,
                Patch = normalizedPatch,
                Items = [],
            };
        }

        var verdicts = await scoped
            .Where(verdict => verdict.Patch == servedPatch)
            .OrderByDescending(verdict => verdict.PickRate)
            .ToListAsync(ct);

        return new ChampionItemContextResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = servedPatch,
            Items = [.. verdicts.Select(ToReadModel)],
        };
    }

    private static ChampionItemContextItemReadModel ToReadModel(ChampionItemContextVerdict verdict)
        => new()
        {
            Slot = verdict.Slot.ToString(),
            ItemId = verdict.ItemId,
            Class = verdict.Class.ToString(),
            Games = verdict.Games,
            SlotGames = verdict.SlotGames,
            PickRate = verdict.PickRate,
            WinRate = verdict.Games > 0 ? verdict.Wins / (double)verdict.Games : null,
            PatchWindow = verdict.PatchWindow,
            Axes = [.. verdict.Axes.Select(ToReadModel)],
        };

    private static ChampionItemContextAxisReadModel ToReadModel(ItemContextAxisFinding finding)
        => new()
        {
            Axis = finding.Axis.ToString(),
            Bucket = finding.Bucket.ToString(),
            DraftTime = ItemContextAxes.IsDraftTime(finding.Axis),
            GamesIn = finding.GamesIn,
            TotalIn = finding.TotalIn,
            GamesOut = finding.GamesOut,
            TotalOut = finding.TotalOut,
            RateIn = finding.RateIn,
            RateOut = finding.RateOut,
            Lift = finding.Lift,
            PatchWindow = finding.PatchWindow,
        };
}
