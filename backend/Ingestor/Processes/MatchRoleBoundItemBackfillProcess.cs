using Data;
using Data.BuildFacts;
using Data.Entities;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Fills <c>match_participants."RoleBoundItemId"</c> for bot-lane rows ingested before
/// Riot's role-bound slot was recorded (#1612). A bot laner's boots live in that slot, so
/// without it those rows show no boots at all; <see cref="RoleBoundBootsInference"/>
/// recovers them from the item timeline already stored on the row. The other roles'
/// slot holds a quest reward the timeline does not name, so their legacy rows stay
/// <c>null</c> and read as empty. New rows arrive with Riot's value, so steady state is
/// an empty read of the partial index this queue sits on.
/// </summary>
public sealed class MatchRoleBoundItemBackfillProcess(
    ILogger<MatchRoleBoundItemBackfillProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IItemMetadataProvider itemMetadataProvider) : IIngestorProcess
{
    // A literal, not a parameter: the partial index's predicate only serves a query
    // whose filter carries the same constant.
    private const string BottomPosition = "BOTTOM";

    // Each row carries its item timeline, the heavy column, so the batch stays small;
    // the per-run cap keeps one run short while a ~1M-row backlog drains over a day of
    // 20-minute cycles.
    private const int BatchSize = 2000;
    private const int MaxBatchesPerRun = 25;

    public string Name => "MatchRoleBoundItemBackfill";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var resolved = 0;
        var inferredBoots = 0;
        var batches = 0;

        while (batches < MaxBatchesPerRun)
        {
            var rows = await db.MatchParticipants
                .AsNoTracking()
                .Where(p => p.RoleBoundItemId == null && p.TeamPosition == BottomPosition)
                .OrderBy(p => p.Id)
                .Take(BatchSize)
                .Join(
                    db.Matches.AsNoTracking(),
                    p => p.MatchId,
                    m => m.Id,
                    (p, m) => new PendingRow(
                        p.Id,
                        m.GameVersion,
                        new[] { p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5 },
                        p.ItemEvents))
                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                break;
            }

            batches++;

            var idsByValue = new Dictionary<int, List<Guid>>();
            foreach (var row in rows)
            {
                var metadata = await itemMetadataProvider.GetItemsAsync(row.GameVersion, ct);
                var value = RoleBoundBootsInference.Infer(row.ItemEvents, row.InventorySlots, metadata);
                if (!idsByValue.TryGetValue(value, out var ids))
                {
                    ids = [];
                    idsByValue[value] = ids;
                }
                ids.Add(row.Id);
            }

            foreach (var (value, ids) in idsByValue)
            {
                var updated = await db.MatchParticipants
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.RoleBoundItemId, (int?)value), ct);
                resolved += updated;
                inferredBoots += value > 0 ? updated : 0;
            }

            if (rows.Count < BatchSize)
            {
                break;
            }
        }

        if (resolved > 0)
        {
            logger.LogInformation(
                "Role-bound backfill resolved {Resolved} bot-lane participant(s), {InferredBoots} with boots, in {Batches} batch(es).",
                resolved,
                inferredBoots,
                batches);
        }

        return new RoleBoundItemBackfillSummary(resolved, inferredBoots, batches);
    }

    private sealed record PendingRow(Guid Id, string GameVersion, int[] InventorySlots, List<ItemEvent> ItemEvents);
}
