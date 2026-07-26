using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface IMatchClaimService
{
    Task<List<AccountKey>> ClaimAsync(
        IReadOnlyCollection<string> platforms,
        int batchSize,
        double establishedMainShare,
        TimeSpan lease,
        CancellationToken ct);
}
