using Core.Lol.Spells;
using Data;
using Data.BuildFacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Loads the top-K participants' raw build data and folds it into a
/// win-weighted <see cref="CompositionBuildRecommendation"/>. Fact extraction
/// reuses the same primitives as the champion-page aggregation pipeline
/// (<see cref="StarterItemAnalyzer"/>, <see cref="FinalBuildResolver"/>,
/// <see cref="BootsResolver"/>, <see cref="SkillOrderBuilder"/>), so both
/// features read a game identically; the folding itself is the pure
/// <see cref="CompositionBuildAggregator"/>.
/// </summary>
public sealed class CompositionBuildQueryService(
    TrueMainDbContext db,
    IItemMetadataProvider itemMetadataProvider,
    IOptions<CompositionSearchOptions> searchOptions,
    ILogger<CompositionBuildQueryService> logger)
    : ICompositionBuildQueryService
{
    public async Task<CompositionBuildRecommendation> AggregateAsync(
        int championId,
        string position,
        IReadOnlyList<CompositionMatchRef> matches,
        CancellationToken ct)
    {
        if (matches.Count == 0)
        {
            return new CompositionBuildRecommendation();
        }

        var options = searchOptions.Value;
        var matchIds = matches.Select(m => m.MatchId).Distinct().ToList();
        var selectedKeys = matches
            .Select(m => (m.MatchId, m.ParticipantId))
            .ToHashSet();

        // The champion+position filter re-identifies the selected rows without
        // a tuple IN — at most one row per match can be the searched champion
        // at the searched position, and the key set drops any stray leftover.
        var rows = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId)
                && p.ChampionId == championId
                && p.TeamPosition == position)
            .Join(
                db.Matches,
                p => p.MatchId,
                m => m.Id,
                (p, m) => new
                {
                    p.MatchId,
                    p.ParticipantId,
                    p.Win,
                    m.GameVersion,
                    p.Item0,
                    p.Item1,
                    p.Item2,
                    p.Item3,
                    p.Item4,
                    p.Item5,
                    p.Item6,
                    p.ItemEvents,
                    p.SkillEvents,
                    p.Summoner1Id,
                    p.Summoner2Id,
                    p.PrimaryStyleId,
                    p.SubStyleId,
                    p.PerksOffense,
                    p.PerksFlex,
                    p.PerksDefense,
                })
            .ToListAsync(ct);
        rows.RemoveAll(r => !selectedKeys.Contains((r.MatchId, r.ParticipantId)));

        var runePages = await LoadRunePagesAsync(matchIds, selectedKeys, ct);

        var facts = new List<CompositionParticipantFacts>(rows.Count);
        foreach (var row in rows)
        {
            var itemMetadata = await GetItemMetadataAsync(row.GameVersion, ct);
            runePages.TryGetValue((row.MatchId, row.ParticipantId), out var selections);

            if (itemMetadata is null)
            {
                // No metadata for this patch: the item dimensions abstain for
                // this game, everything else still votes.
                facts.Add(new CompositionParticipantFacts
                {
                    Win = row.Win,
                    Spell1Id = new SummonerSpellPair(row.Summoner1Id, row.Summoner2Id).Canonical().Spell1Id,
                    Spell2Id = new SummonerSpellPair(row.Summoner1Id, row.Summoner2Id).Canonical().Spell2Id,
                    SkillOrderKey = SkillOrderBuilder.Build(row.SkillEvents),
                    RunePage = BuildRunePageFacts(
                        row.PrimaryStyleId, row.SubStyleId,
                        row.PerksOffense, row.PerksFlex, row.PerksDefense, selections),
                });
                continue;
            }

            int[] finalItems = [row.Item0, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item6];
            var starterAnalysis = StarterItemAnalyzer.Analyze(row.ItemEvents, finalItems, itemMetadata);
            var spellPair = new SummonerSpellPair(row.Summoner1Id, row.Summoner2Id).Canonical();

            facts.Add(new CompositionParticipantFacts
            {
                Win = row.Win,
                BuildItems = FinalBuildResolver.Resolve(
                    row.ItemEvents, finalItems, starterAnalysis.Items, itemMetadata),
                BootsItemId = BootsResolver.Resolve(
                    row.ItemEvents, finalItems, starterAnalysis.Items, itemMetadata),
                StarterItems = starterAnalysis.Items,
                Spell1Id = spellPair.Spell1Id,
                Spell2Id = spellPair.Spell2Id,
                SkillOrderKey = SkillOrderBuilder.Build(row.SkillEvents),
                RunePage = BuildRunePageFacts(
                    row.PrimaryStyleId, row.SubStyleId,
                    row.PerksOffense, row.PerksFlex, row.PerksDefense, selections),
            });
        }

        return CompositionBuildAggregator.Aggregate(
            facts, options.WinWeight, options.SituationalItemCount);
    }

    /// <summary>
    /// Loads the ordered perk selections of the selected participants, keyed
    /// by (match, participant) — the same primary/sub style split the pattern
    /// aggregation pipeline hydrates from
    /// <c>participant_perk_selections</c> × <c>perk_selection_catalogs</c>.
    /// </summary>
    private async Task<Dictionary<(string MatchId, int ParticipantId), List<PerkSelectionRow>>>
        LoadRunePagesAsync(
            List<string> matchIds,
            HashSet<(string, int)> selectedKeys,
            CancellationToken ct)
    {
        var selections = await db.ParticipantPerkSelections
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId))
            .Join(
                db.PerkSelectionCatalogs,
                s => s.PerkSelectionCatalogId,
                c => c.Id,
                (s, c) => new PerkSelectionRow(
                    s.MatchId, s.ParticipantId, c.StyleDescription, c.SelectionIndex, c.PerkId))
            .ToListAsync(ct);

        return selections
            .Where(s => selectedKeys.Contains((s.MatchId, s.ParticipantId)))
            .GroupBy(s => (s.MatchId, s.ParticipantId))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SelectionIndex).ToList());
    }

    /// <summary>
    /// Assembles the full rune page from the participant's style/shard columns
    /// plus its ordered perk selections. Null when the selections are missing
    /// or incomplete — the rune dimension then abstains for this game.
    /// </summary>
    private static CompositionRunePageFacts? BuildRunePageFacts(
        int primaryStyleId,
        int subStyleId,
        int perksOffense,
        int perksFlex,
        int perksDefense,
        List<PerkSelectionRow>? selections)
    {
        if (selections is null)
        {
            return null;
        }

        var primary = selections
            .Where(s => string.Equals(s.StyleDescription, "primaryStyle", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.PerkId)
            .ToList();
        var secondary = selections
            .Where(s => string.Equals(s.StyleDescription, "subStyle", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.PerkId)
            .ToList();

        if (primary.Count < 4 || secondary.Count < 2)
        {
            return null;
        }

        return new CompositionRunePageFacts(
            primaryStyleId,
            primary[0],
            primary[1],
            primary[2],
            primary[3],
            subStyleId,
            secondary[0],
            secondary[1],
            perksOffense,
            perksFlex,
            perksDefense);
    }

    /// <summary>
    /// Item metadata for the game's patch, or null when CommunityDragon has
    /// nothing for it (stale patches age out of the CDN) — the caller degrades
    /// to item-less facts instead of failing the whole recommendation.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, ItemMetadata>?> GetItemMetadataAsync(
        string gameVersion,
        CancellationToken ct)
    {
        try
        {
            return await itemMetadataProvider.GetItemsAsync(gameVersion, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex, "Item metadata unavailable for game version {GameVersion}; skipping item facts.", gameVersion);
            return null;
        }
    }

    private sealed record PerkSelectionRow(
        string MatchId,
        int ParticipantId,
        string StyleDescription,
        int SelectionIndex,
        int PerkId);
}
