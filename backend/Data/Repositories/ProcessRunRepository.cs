using Data.Entities;

namespace Data.Repositories;

public sealed class ProcessRunRepository(TrueMainDbContext db) : IProcessRunRepository
{
    public void Add(ProcessRun run)
        => db.ProcessRuns.Add(run);
}
