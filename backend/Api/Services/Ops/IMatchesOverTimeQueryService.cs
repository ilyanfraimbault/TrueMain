using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>Allowed x-axis granularities for the matches-over-time histogram.</summary>
public enum MatchTimeGranularity
{
    Week,
    Month,
    Year,
    Patch
}

public interface IMatchesOverTimeQueryService
{
    Task<IReadOnlyList<MatchTimeBucket>> GetAsync(
        MatchTimeGranularity granularity,
        string? region,
        CancellationToken ct);
}
