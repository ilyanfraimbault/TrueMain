using Data.Repositories;
using Ingestor.Options;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes.Components.Coverage;

public sealed class ChampionCoverageProvider(IOptions<CoverageOptions> options) : IChampionCoverageProvider
{
    public async Task<ChampionCoverageSnapshot> GetSnapshotAsync(IDataSession session, CancellationToken ct)
    {
        var mainsByChampion = await session.MainChampionStats.GetMainCountsByChampionAsync(ct);

        // Cold start: with no mains anywhere there is no per-champion signal to act on,
        // so fall back to the neutral snapshot explicitly instead of relying on an
        // empty-dictionary code path inside the snapshot.
        return mainsByChampion.Count == 0
            ? ChampionCoverageSnapshot.Empty
            : new ChampionCoverageSnapshot(mainsByChampion, options.Value.TargetMainsPerChampion);
    }
}
