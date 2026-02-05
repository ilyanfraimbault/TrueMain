using Data;
using Ingestor;
using Ingestor.Options;
using Ingestor.Riot;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RiotOptions>(builder.Configuration.GetSection("Riot"));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection("Seed"));
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection("Discovery"));

builder.Services.AddHttpClient<IRiotMatchClient, RiotMatchClient>();
builder.Services.AddHttpClient<IRiotPlatformClient, RiotPlatformClient>();

builder.Services.AddDbContextFactory<TrueMainDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("TrueMain");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Missing connection string. Add ConnectionStrings:TrueMain to user secrets.");
    }

    options.UseNpgsql(connectionString);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
