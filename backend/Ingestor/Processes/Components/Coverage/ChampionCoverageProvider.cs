using Data.Repositories;
using Ingestor.Options;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes.Components.Coverage;

public sealed class ChampionCoverageProvider(IOptions<CoverageOptions> options) : IChampionCoverageProvider
{
    public async Task<ChampionCoverageSnapshot> GetSnapshotAsync(IDataSession session, CancellationToken ct)
    {
        var mainsByChampion = await session.MainChampionStats.GetMainCountsByChampionAsync(ct);
        return new ChampionCoverageSnapshot(mainsByChampion, options.Value.TargetMainsPerChampion);
    }
}
