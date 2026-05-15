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
            var optionsFromArgs = new DbContextOptionsBuilder<TrueMainDbContext>();
            optionsFromArgs.UseNpgsql(connectionFromArgs);
            return new TrueMainDbContext(optionsFromArgs.Options);
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("TrueMain");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing connection string. Add ConnectionStrings:TrueMain to user secrets.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<TrueMainDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

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
