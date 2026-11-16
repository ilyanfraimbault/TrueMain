using TrueMain.Configuration;
using TrueMain.Options;

namespace TrueMain.Services.Ops.Database;

public static class OpsDatabaseServiceCollectionExtensions
{
    /// <summary>Registers the table sizes and the storage-growth history behind <c>/database</c>.</summary>
    public static IServiceCollection AddTrueMainOpsDatabase(this IServiceCollection services)
    {
        services.AddApiOptions<StorageHistoryOptions>();
        services.AddScoped<ITableStatsQueryService, TableStatsQueryService>();
        services.AddScoped<IDbStorageHistoryQueryService, DbStorageHistoryQueryService>();
        return services;
    }
}
