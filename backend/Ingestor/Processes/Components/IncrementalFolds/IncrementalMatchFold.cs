using System.Linq.Expressions;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.IncrementalFolds;

/// <summary>
/// The drain loop shared by the incremental per-match folds — ban, matchup and synergy
/// (#1239). Each of them folds every pending match of the analysed queue exactly once:
/// select the next batch of match ids whose per-fold flag is still false (oldest id
/// first), fold it on a fresh <see cref="TrueMainDbContext"/>, and stop when the backlog
/// is drained, a batch comes back short, or <c>maxPerRun</c> matches have been folded.
/// What a fold does with its batch — the reads, the accumulation, the additive upserts
/// and flipping its flag in one transaction — stays in the fold.
/// </summary>
internal static class IncrementalMatchFold
{
    /// <summary>
    /// Drains <paramref name="pending"/> matches of <paramref name="queueId"/> in batches of
    /// <paramref name="batchSize"/>, handing each batch to <paramref name="foldBatch"/> on its
    /// own context. <paramref name="foldBatch"/> must flip the fold's flag for every id it is
    /// given, otherwise the next iteration selects the same batch again.
    /// </summary>
    public static Task<IncrementalFoldProgress> RunAsync(
        IDbContextFactory<TrueMainDbContext> dbContextFactory,
        int queueId,
        Expression<Func<Match, bool>> pending,
        int batchSize,
        int maxPerRun,
        Func<TrueMainDbContext, List<string>, CancellationToken, Task> foldBatch,
        CancellationToken ct)
        => DrainAsync(
            batchSize,
            maxPerRun,
            async (take, token) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);

                // Each fold keeps a partial index on its pending flag, so this stays an
                // index scan over the tail ingested since the previous run.
                var matchIds = await db.Matches
                    .AsNoTracking()
                    .Where(m => m.QueueId == queueId)
                    .Where(pending)
                    .OrderBy(m => m.Id)
                    .Take(take)
                    .Select(m => m.Id)
                    .ToListAsync(token);

                if (matchIds.Count == 0)
                {
                    return 0;
                }

                await foldBatch(db, matchIds, token);
                return matchIds.Count;
            },
            ct);

    /// <summary>
    /// The paging arithmetic on its own: asks <paramref name="foldNextBatch"/> for at most
    /// <c>take</c> matches per call and returns how many it folded. A <paramref name="maxPerRun"/>
    /// of 0 means unbounded; otherwise the last batch is shrunk so the run never exceeds it.
    /// </summary>
    public static async Task<IncrementalFoldProgress> DrainAsync(
        int batchSize,
        int maxPerRun,
        Func<int, CancellationToken, Task<int>> foldNextBatch,
        CancellationToken ct)
    {
        var processed = 0;
        var batches = 0;

        while (maxPerRun == 0 || processed < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - processed);

            var folded = await foldNextBatch(take, ct);
            if (folded == 0)
            {
                break;
            }

            processed += folded;
            batches++;

            if (folded < take)
            {
                break;
            }
        }

        return new IncrementalFoldProgress(processed, batches);
    }
}

/// <summary>How many matches one run of an incremental fold folded, over how many batches.</summary>
internal readonly record struct IncrementalFoldProgress(int Matches, int Batches);
