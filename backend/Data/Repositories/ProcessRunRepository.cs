using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class ProcessRunRepository(TrueMainDbContext db) : IProcessRunRepository
{
    public void Add(ProcessRun run)
        => db.ProcessRuns.Add(run);

    public async Task<ProcessRun?> GetByIdAsync(Guid id, CancellationToken ct)
        // Tracked: the caller mutates Status/FinishedAtUtc on the returned
        // instance and then SaveChanges to finalise the run.
        => await db.ProcessRuns.FindAsync([id], ct);

    public async Task<IReadOnlyList<ProcessRun>> GetRunningAsync(CancellationToken ct)
        // Tracked: the caller flips Status/FinishedAtUtc/Error on each returned
        // instance and then SaveChanges to abandon the orphaned runs.
        => await db.ProcessRuns
            .Where(run => run.Status == ProcessRunStatus.Running)
            .ToListAsync(ct);
}
