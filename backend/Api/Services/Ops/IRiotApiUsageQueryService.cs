using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IRiotApiUsageQueryService
{
    /// <summary>
    /// Builds the Riot API usage read-model for the given relative
    /// <paramref name="window"/> (<c>1h</c> / <c>24h</c> / <c>7d</c>; unknown or
    /// null defaults to 24h), optionally restricted to a single
    /// <paramref name="endpoint"/> key.
    /// </summary>
    Task<RiotApiUsageReadModel> GetAsync(string? window, string? endpoint, CancellationToken ct);
}
