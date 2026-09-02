using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions;

/// <summary>
/// When the ingestor's aggregation lane last published champion numbers.
/// </summary>
public interface IChampionAggregationStamp
{
    /// <summary>
    /// The newest aggregation timestamp, or <see langword="null"/> when nothing has
    /// been aggregated yet.
    /// </summary>
    Task<DateTime?> GetLatestAsync(CancellationToken ct);
}

/// <summary>
/// Reads the stamp from <c>champion_aggregate_scopes</c>, whose every row the pattern
/// aggregation re-stamps as it writes it (#1368).
/// </summary>
/// <remarks>
/// Deliberately not cached here: <see cref="ChampionReadCache"/> owns the caching and
/// the single flight around this call, so there is exactly one place that decides how
/// often the question is asked. It has to stay a question worth asking that often — a
/// <c>max()</c> over one column of a table that holds tens of thousands of rows, not
/// something that grows with traffic.
/// </remarks>
public sealed class ChampionAggregationStamp(TrueMainDbContext db) : IChampionAggregationStamp
{
    /// <inheritdoc />
    public Task<DateTime?> GetLatestAsync(CancellationToken ct)
        => db.ChampionAggregateScopes
            .AsNoTracking()
            .MaxAsync(scope => (DateTime?)scope.AggregatedAtUtc, ct);
}
