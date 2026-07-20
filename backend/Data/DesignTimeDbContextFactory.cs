using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrueMainDbContext>
{
    public TrueMainDbContext CreateDbContext(string[] args)
    {
        var connectionFromArgs = TryGetConnectionFromArgs(args);
        if (!string.IsNullOrWhiteSpace(connectionFromArgs))
        {
            return CreateDbContext(connectionFromArgs);
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        return CreateDbContext(DataServiceCollectionExtensions.GetRequiredConnectionString(configuration));
    }

    private static TrueMainDbContext CreateDbContext(string connectionString)
    {
        // Build the same EnableDynamicJson data source as the runtime hosts so
        // migrations / scaffolding that touch jsonb columns (item and skill events,
        // position breakdowns, summaries, starter items) map identically at design
        // time and at runtime.
        var dataSource = DataServiceCollectionExtensions.BuildDataSource(connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql(dataSource);

        return new TrueMainDbContext(optionsBuilder.Options);
    }

    private static string? TryGetConnectionFromArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--connection", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            const string prefix = "--connection=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }
}
