using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface IAccountValidationService
{
    Task ValidateAsync(AccountKey account, CancellationToken ct);
    Task RevertAsync(AccountKey account, CancellationToken ct);
}
