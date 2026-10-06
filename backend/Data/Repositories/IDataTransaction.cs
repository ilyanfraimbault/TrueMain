namespace Data.Repositories;

/// <summary>
/// A database transaction opened by <see cref="IDataSession.BeginTransactionAsync"/>. It exposes
/// only the commit / rollback lifecycle, so callers above <c>Data</c> never reach the underlying
/// EF Core or ADO.NET transaction (#241).
/// </summary>
/// <remarks>
/// Disposing without a prior <see cref="CommitAsync"/> rolls the transaction back.
/// </remarks>
public interface IDataTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
