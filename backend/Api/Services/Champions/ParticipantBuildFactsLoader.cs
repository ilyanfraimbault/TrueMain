using Core.Lol.Spells;
using Data;
using Data.BuildFacts;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions;

/// <summary>
/// Turns a selected set of participants into build facts — items, boots, starters,
/// summoner spells, skill order, rune page — using the same primitives as the
/// champion-page aggregation pipeline (<see cref="StarterItemAnalyzer"/>,
/// <see cref="FinalBuildResolver"/>, <see cref="BootsResolver"/>,
/// <see cref="SkillOrderBuilder"/>).
///
/// <para>
/// Extracted so every live build path reads a game identically. The composition
/// recommendation (#921) and the matchup-scoped champion page (#923) select different
/// games — a similarity-ranked top-K versus everyone who faced a given opponent — but a
/// game must not mean two different builds depending on which feature is looking at it.
/// </para>
/// </summary>
public sealed class ParticipantBuildFactsLoader(
    TrueMainDbContext db,
    IItemMetadataProvider itemMetadataProvider,
    ILogger<ParticipantBuildFactsLoader> logger)
{
    /// <summary>
    /// Loads facts for <paramref name="keys"/>, keeping the caller's order.
    /// </summary>
    /// <param name="keys">
    /// The selected participants, in the order the caller wants them folded. Order is
    /// not cosmetic: the aggregators break exact ties on insertion order, so a caller
    /// that ranks its games must pass them ranked.
    /// </param>
    /// <param name="championId">Champion the facts are about (re-identifies the rows).</param>
    /// <param name="position">Position the facts are about.</param>
    /// <param name="weightFor">
    /// Per-participant vote multiplier, or null for an unweighted fold (every game
    /// counts once — what a matchup slice wants, since no game is "closer" than another).
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    public async Task<List<CompositionParticipantFacts>> LoadAsync(
        IReadOnlyList<ParticipantKey> keys,
        int championId,
        string position,
        Func<ParticipantKey, double>? weightFor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return [];
        }

        var rankByKey = new Dictionary<ParticipantKey, int>(keys.Count);
        foreach (var key in keys)
        {
            rankByKey.TryAdd(key, rankByKey.Count);
        }

        var matchIds = keys.Select(key => key.MatchId).Distinct().ToList();

        // The champion+position filter re-identifies the selected rows without a tuple
        // IN — at most one row per match can be the searched champion at the searched
        // position, and the key set drops any stray leftover.
        var rows = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId)
                && p.ChampionId == championId
                && p.TeamPosition == position)
            .Join(
                db.Matches,
                p => p.MatchId,
                m => m.Id,
                (p, m) => new ParticipantBuildRow(
                    p.MatchId,
                    p.ParticipantId,
                    p.Win,
                    m.GameVersion,
                    new[] { p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5, p.Item6 },
                    p.ItemEvents,
                    p.SkillEvents,
                    p.Summoner1Id,
                    p.Summoner2Id,
                    p.PrimaryStyleId,
                    p.SubStyleId,
                    p.PerksOffense,
                    p.PerksFlex,
                    p.PerksDefense))
            .ToListAsync(ct);

        rows.RemoveAll(row => !rankByKey.ContainsKey(new ParticipantKey(row.MatchId, row.ParticipantId)));
        rows.Sort((left, right) => rankByKey[new ParticipantKey(left.MatchId, left.ParticipantId)]
            .CompareTo(rankByKey[new ParticipantKey(right.MatchId, right.ParticipantId)]));

        var runePages = await LoadRunePagesAsync(matchIds, rankByKey.Keys.ToHashSet(), ct);

        var facts = new List<CompositionParticipantFacts>(rows.Count);
        foreach (var row in rows)
        {
            var key = new ParticipantKey(row.MatchId, row.ParticipantId);
            var weight = weightFor?.Invoke(key) ?? 1d;
            var itemMetadata = await GetItemMetadataAsync(row.GameVersion, ct);
            runePages.TryGetValue(key, out var selections);

            var spellPair = new SummonerSpellPair(row.Summoner1Id, row.Summoner2Id).Canonical();
            var runePage = BuildRunePageFacts(
                row.PrimaryStyleId, row.SubStyleId,
                row.PerksOffense, row.PerksFlex, row.PerksDefense, selections);

            if (itemMetadata is null)
            {
                // No metadata for this patch: the item dimensions abstain for this game,
                // everything else still votes.
                facts.Add(new CompositionParticipantFacts
                {
                    Win = row.Win,
                    SimilarityWeight = weight,
                    Spell1Id = spellPair.Spell1Id,
                    Spell2Id = spellPair.Spell2Id,
                    SkillOrderKey = SkillOrderBuilder.Build(row.SkillEvents),
                    RunePage = runePage,
                });
                continue;
            }

            var starterAnalysis = StarterItemAnalyzer.Analyze(row.ItemEvents, row.FinalItems, itemMetadata);

            facts.Add(new CompositionParticipantFacts
            {
                Win = row.Win,
                SimilarityWeight = weight,
                BuildItems = FinalBuildResolver.Resolve(
                    row.ItemEvents, row.FinalItems, starterAnalysis.Items, itemMetadata),
                BootsItemId = BootsResolver.Resolve(
                    row.ItemEvents, row.FinalItems, starterAnalysis.Items, itemMetadata),
                StarterItems = starterAnalysis.Items,
                Spell1Id = spellPair.Spell1Id,
                Spell2Id = spellPair.Spell2Id,
                SkillOrderKey = SkillOrderBuilder.Build(row.SkillEvents),
                RunePage = runePage,
            });
        }

        return facts;
    }

    /// <summary>
    /// Loads the ordered perk selections of the selected participants, keyed by
    /// (match, participant) — the same primary/sub style split the pattern aggregation
    /// pipeline hydrates from <c>participant_perk_selections</c> ×
    /// <c>perk_selection_catalogs</c>.
    /// </summary>
    private async Task<Dictionary<ParticipantKey, List<PerkSelectionRow>>> LoadRunePagesAsync(
        List<string> matchIds,
        HashSet<ParticipantKey> selectedKeys,
        CancellationToken ct)
    {
        var selections = await db.ParticipantPerkSelections
            .AsNoTracking()
            .Where(selection => matchIds.Contains(selection.MatchId))
            .Join(
                db.PerkSelectionCatalogs,
                selection => selection.PerkSelectionCatalogId,
                catalog => catalog.Id,
                (selection, catalog) => new PerkSelectionRow(
                    selection.MatchId,
                    selection.ParticipantId,
                    catalog.StyleDescription,
                    catalog.SelectionIndex,
                    catalog.PerkId))
            .ToListAsync(ct);

        return selections
            .Where(selection => selectedKeys.Contains(new ParticipantKey(selection.MatchId, selection.ParticipantId)))
            .GroupBy(selection => new ParticipantKey(selection.MatchId, selection.ParticipantId))
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.SelectionIndex).ToList());
    }

    /// <summary>
    /// Assembles the full rune page from the participant's style/shard columns plus its
    /// ordered perk selections. Null when the selections are missing or incomplete — the
    /// rune dimension then abstains for this game.
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
            .Where(selection => string.Equals(selection.StyleDescription, "primaryStyle", StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.PerkId)
            .ToList();
        var secondary = selections
            .Where(selection => string.Equals(selection.StyleDescription, "subStyle", StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.PerkId)
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
    /// Item metadata for the game's patch, or null when CommunityDragon has nothing for
    /// it (stale patches age out of the CDN) — the caller degrades to item-less facts
    /// instead of failing the whole read.
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

    private sealed record ParticipantBuildRow(
        string MatchId,
        int ParticipantId,
        bool Win,
        string GameVersion,
        int[] FinalItems,
        List<ItemEvent> ItemEvents,
        List<SkillEvent> SkillEvents,
        int Summoner1Id,
        int Summoner2Id,
        int PrimaryStyleId,
        int SubStyleId,
        int PerksOffense,
        int PerksFlex,
        int PerksDefense);

    private sealed record PerkSelectionRow(
        string MatchId,
        int ParticipantId,
        string StyleDescription,
        int SelectionIndex,
        int PerkId);
}

/// <summary>Identifies one participant row: a match plus its participant slot.</summary>
public readonly record struct ParticipantKey(string MatchId, int ParticipantId);
