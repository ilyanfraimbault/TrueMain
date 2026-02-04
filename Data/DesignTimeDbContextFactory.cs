using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrueMainDbContext>
{
    public TrueMainDbContext CreateDbContext(string[] args)
    {
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
}
