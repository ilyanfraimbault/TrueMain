using TrueMain.Options;

namespace TrueMain.Services.Ops.Accounts;

public static class OpsAccountsServiceCollectionExtensions
{
    /// <summary>Registers the seed requests and the account explorer.</summary>
    public static IServiceCollection AddTrueMainOpsAccounts(this IServiceCollection services)
    {
        services.AddMainAnalysisOptions();
        services.AddScoped<ISeedRequestService, SeedRequestService>();
        services.AddScoped<ISeedRequestQueryService, SeedRequestQueryService>();
        services.AddScoped<IAccountExplorerQueryService, AccountExplorerQueryService>();
        services.AddScoped<IAccountFreshnessQueryService, AccountFreshnessQueryService>();
        return services;
    }
}
