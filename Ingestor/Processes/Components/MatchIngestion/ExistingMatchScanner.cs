using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

internal static class ExistingMatchScanner
{
    public static async Task<ExistingMatchScan> ScanAsync(
        IDataSession session,
        IReadOnlyList<string> allMatchIds,
        CancellationToken ct)
    {
        var existingSet = await session.Matches.GetExistingMatchIdsAsync(allMatchIds, ct);
        var existing = allMatchIds.Where(id => existingSet.Contains(id)).ToList();
        var fresh = allMatchIds.Where(id => !existingSet.Contains(id)).ToList();
        return new ExistingMatchScan(existing, fresh);
    }
}

internal sealed record ExistingMatchScan(IReadOnlyList<string> Existing, IReadOnlyList<string> Fresh);
