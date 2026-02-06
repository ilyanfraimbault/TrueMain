using Data;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Riot;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RiotOptions>(builder.Configuration.GetSection("Riot"));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection("Seed"));
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection("Discovery"));
builder.Services.Configure<ScoringOptions>(builder.Configuration.GetSection("Scoring"));
builder.Services.Configure<MatchIngestionOptions>(builder.Configuration.GetSection("MatchIngestion"));
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection("Job"));

builder.Services.AddHttpClient<IRiotMatchClient, RiotMatchClient>();
builder.Services.AddHttpClient<IRiotPlatformClient, RiotPlatformClient>();

builder.Services.AddSingleton<DiscoveryProcess>();
builder.Services.AddSingleton<ScoringProcess>();
builder.Services.AddSingleton<MatchIngestionProcess>();

builder.Services.AddDbContextFactory<TrueMainDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("TrueMain");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Missing connection string. Add ConnectionStrings:TrueMain to user secrets.");
    }

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.EnableDynamicJson();
    var dataSource = dataSourceBuilder.Build();

    options.UseNpgsql(dataSource);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
