namespace Data.Repositories;

public interface IDiscoveryCursorRepository
{
    /// <summary>Current offset for the platform, or null when no cursor exists yet.</summary>
    Task<int?> GetOffsetAsync(string platformId, CancellationToken ct);

    /// <summary>
    /// Insert or update the platform's cursor offset. Written immediately with a single
    /// statement, so it does not need (and is not affected by) a later SaveChanges.
    /// </summary>
    Task UpsertOffsetAsync(string platformId, int offset, DateTime nowUtc, CancellationToken ct);
}
