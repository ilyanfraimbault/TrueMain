using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Repositories;

internal sealed class DataTransaction(IDbContextTransaction transaction) : IDataTransaction
{
    public Task CommitAsync(CancellationToken ct)
        => transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct)
        => transaction.RollbackAsync(ct);

    public ValueTask DisposeAsync()
        => transaction.DisposeAsync();
}
