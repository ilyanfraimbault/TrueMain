using Data;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Collapses permutation-duplicate rows in <c>champion_dim_rune_pages</c> and puts
/// every surviving row's secondary perks in canonical order (#911).
///
/// <para>
/// The dimension stored the two secondary perks in the player's click order, so one
/// rune page existed as two rows — <c>(8451, 8444)</c> and <c>(8444, 8451)</c> — and
/// the 11-column unique index does not catch a permutation. A page's games and wins
/// were therefore split across both rows, roughly halving its displayed pick rate and
/// distorting the top-N selection. Measured on production: 20 370 duplicate pairs,
/// 48% of the dimension, with 88% of <c>champion_aggregate_patterns</c> pointing at a
/// duplicated page.
/// </para>
///
/// <para>
/// <b>Why a pipeline step and not a migration.</b> The merge rewrites hundreds of
/// thousands of pattern rows. Prod applies migrations on startup, so doing it there
/// would risk the command timeout and a crash-loop (see CLAUDE.md); as a step it is
/// batched, observable through <c>process_runs</c>, and resumable — an interrupted run
/// finds the remaining groups next pass.
/// </para>
///
/// <para>
/// <b>Why it is mandatory rather than opportunistic.</b> Canonicalising the reader only
/// fixes pages written from now on. Aggregates for retired patches are frozen and can
/// never be recomputed (#466), so their split rows would stay split forever unless
/// merged in place.
/// </para>
///
/// <para>
/// <b>Why it also rewrites non-duplicated rows.</b> A page that only ever appeared in
/// one order is not split, so merging has nothing to do for it — but left in the
/// player's order, the reader's new canonical lookup would miss it and mint a second
/// row, re-creating the very duplication this fixes. The final pass therefore
/// normalises every remaining row; by then no two rows share a canonical key, so it
/// cannot collide.
/// </para>
///
/// <para>
/// Idempotent, and cheap once drained: the merge selects groups that still have more
/// than one row for the canonical key and the normalisation selects rows still out of
/// order, so a steady-state run does two indexed scans and writes nothing. It runs
/// before <see cref="ChampionPatternAggregationProcess"/> so a pass never aggregates
/// into a dimension it is about to rewrite.
/// </para>
/// </summary>
public sealed class RunePageDeduplicationProcess(
    ILogger<RunePageDeduplicationProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory) : IIngestorProcess
{
    /// <summary>
    /// Duplicate groups merged per transaction. Each group rewrites the pattern rows of
    /// one rune page, so a few hundred groups keeps a transaction's lock footprint and
    /// WAL bounded while still draining 20 370 groups in a handful of passes.
    /// </summary>
    private const int GroupBatchSize = 250;

    /// <summary>
    /// The canonical-key partition, spelled once. <c>LEAST</c>/<c>GREATEST</c> on the
    /// secondary pair is what collapses the two permutations onto one key without
    /// needing the pair sorted on disk yet.
    /// </summary>
    private const string CanonicalKeyColumns = """
        "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id",
        "PrimaryPerk2Id", "PrimaryPerk3Id", "SecondaryStyleId",
        LEAST("SecondaryPerk1Id", "SecondaryPerk2Id"),
        GREATEST("SecondaryPerk1Id", "SecondaryPerk2Id"),
        "StatOffense", "StatFlex", "StatDefense"
        """;

    public string Name => "RunePageDeduplication";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var mergedGroups = 0;
        var deletedPages = 0;
        var repointedPatterns = 0;
        var foldedPatterns = 0;
        var batches = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var batch = await MergeBatchAsync(db, ct);

            if (batch.Groups == 0)
            {
                break;
            }

            mergedGroups += batch.Groups;
            deletedPages += batch.DeletedPages;
            repointedPatterns += batch.RepointedPatterns;
            foldedPatterns += batch.FoldedPatterns;
            batches++;
        }

        await using var normalizeDb = await dbContextFactory.CreateDbContextAsync(ct);
        var normalizedPages = await NormalizeRemainingAsync(normalizeDb, ct);

        logger.LogInformation(
            "Rune page deduplication summary: groups={Groups}, deletedPages={DeletedPages}, "
            + "repointedPatterns={Repointed}, foldedPatterns={Folded}, normalizedPages={Normalized}, "
            + "batches={Batches}.",
            mergedGroups,
            deletedPages,
            repointedPatterns,
            foldedPatterns,
            normalizedPages,
            batches);

        return new RunePageDeduplicationSummary(
            mergedGroups, deletedPages, repointedPatterns, foldedPatterns, normalizedPages, batches);
    }

    /// <summary>
    /// Merges up to <see cref="GroupBatchSize"/> duplicate groups in one transaction.
    /// </summary>
    /// <remarks>
    /// The statements have to run in this order:
    /// <list type="number">
    /// <item>pick a survivor per canonical key and map every other row to it;</item>
    /// <item>fold the losers' games/wins into the pattern row that already points at
    /// the survivor with an otherwise identical key — repointing those first would
    /// violate the patterns' six-column unique index;</item>
    /// <item>delete the pattern rows just folded in;</item>
    /// <item>repoint the pattern rows that had no counterpart — a plain update, now
    /// that the colliding ones are gone;</item>
    /// <item>delete the losers. The FK is <c>RESTRICT</c>, so this can only succeed
    /// once nothing references them — exactly the check we want. If anything still
    /// did, it throws and the batch rolls back, because a silently orphaned pattern
    /// row would corrupt a scope.</item>
    /// </list>
    /// </remarks>
    private static async Task<BatchResult> MergeBatchAsync(TrueMainDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The survivor is the lowest Id in the group, purely for determinism: the rows
        // are identical apart from the perk order, which the final normalisation pass
        // rewrites anyway.
        await db.Database.ExecuteSqlRawAsync(
            $"""
            CREATE TEMPORARY TABLE rune_page_merge ON COMMIT DROP AS
            WITH grouped AS (
                SELECT
                    "Id",
                    MIN("Id") OVER w AS survivor_id,
                    COUNT(*) OVER w AS group_size
                FROM champion_dim_rune_pages
                WINDOW w AS (PARTITION BY {CanonicalKeyColumns})
            ),
            selected AS (
                SELECT DISTINCT survivor_id
                FROM grouped
                WHERE group_size > 1
                ORDER BY survivor_id
                LIMIT {GroupBatchSize}
            )
            SELECT g."Id" AS loser_id, g.survivor_id
            FROM grouped g
            JOIN selected s ON g.survivor_id = s.survivor_id
            WHERE g."Id" <> g.survivor_id
            """,
            ct);

        var groups = await CountSurvivorsAsync(db, ct);
        if (groups == 0)
        {
            // Nothing left to merge. Roll back rather than commit: the only thing this
            // transaction did was create the (now empty) temp table.
            await transaction.RollbackAsync(ct);
            return BatchResult.Empty;
        }

        var foldedPatterns = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE champion_aggregate_patterns AS keep
            SET "Games" = keep."Games" + folded.games,
                "Wins" = keep."Wins" + folded.wins
            FROM (
                SELECT p."ScopeId", p."BuildId", p."SkillOrderId", p."SpellPairId",
                       p."StarterItemsId", m.survivor_id,
                       SUM(p."Games") AS games, SUM(p."Wins") AS wins
                FROM champion_aggregate_patterns p
                JOIN rune_page_merge m ON p."RunePageId" = m.loser_id
                GROUP BY p."ScopeId", p."BuildId", p."SkillOrderId", p."SpellPairId",
                         p."StarterItemsId", m.survivor_id
            ) AS folded
            WHERE keep."ScopeId" = folded."ScopeId"
              AND keep."BuildId" = folded."BuildId"
              AND keep."RunePageId" = folded.survivor_id
              AND keep."SkillOrderId" = folded."SkillOrderId"
              AND keep."SpellPairId" = folded."SpellPairId"
              AND keep."StarterItemsId" = folded."StarterItemsId"
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM champion_aggregate_patterns AS p
            USING rune_page_merge m
            WHERE p."RunePageId" = m.loser_id
              AND EXISTS (
                  SELECT 1 FROM champion_aggregate_patterns keep
                  WHERE keep."ScopeId" = p."ScopeId"
                    AND keep."BuildId" = p."BuildId"
                    AND keep."RunePageId" = m.survivor_id
                    AND keep."SkillOrderId" = p."SkillOrderId"
                    AND keep."SpellPairId" = p."SpellPairId"
                    AND keep."StarterItemsId" = p."StarterItemsId"
              )
            """,
            ct);

        var repointed = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE champion_aggregate_patterns AS p
            SET "RunePageId" = m.survivor_id
            FROM rune_page_merge m
            WHERE p."RunePageId" = m.loser_id
            """,
            ct);

        var deletedPages = await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM champion_dim_rune_pages
            WHERE "Id" IN (SELECT loser_id FROM rune_page_merge)
            """,
            ct);

        await transaction.CommitAsync(ct);

        return new BatchResult(groups, deletedPages, repointed, foldedPatterns);
    }

    /// <summary>
    /// Rewrites every row still holding its secondary perks in the player's order.
    /// Safe as one statement: the merge above has already collapsed every canonical-key
    /// collision, so swapping the pair cannot hit the unique index. It touches only the
    /// rows actually out of order, so a steady-state run writes nothing.
    /// </summary>
    private static Task<int> NormalizeRemainingAsync(TrueMainDbContext db, CancellationToken ct)
        => db.Database.ExecuteSqlRawAsync(
            """
            UPDATE champion_dim_rune_pages
            SET "SecondaryPerk1Id" = "SecondaryPerk2Id",
                "SecondaryPerk2Id" = "SecondaryPerk1Id"
            WHERE "SecondaryPerk1Id" > "SecondaryPerk2Id"
            """,
            ct);

    private static async Task<int> CountSurvivorsAsync(TrueMainDbContext db, CancellationToken ct)
    {
        var counts = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(DISTINCT survivor_id)::int AS \"Value\" FROM rune_page_merge")
            .ToListAsync(ct);

        return counts.Count == 0 ? 0 : counts[0];
    }

    private readonly record struct BatchResult(
        int Groups,
        int DeletedPages,
        int RepointedPatterns,
        int FoldedPatterns)
    {
        public static BatchResult Empty { get; } = new(0, 0, 0, 0);
    }
}
