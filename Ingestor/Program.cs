using Data;
using Data.Repositories;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Components.MainAnalysis;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Riot;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddValidatedOptions(builder.Configuration);

builder.Services.AddSingleton<IRiotHttpExecutor, RiotHttpExecutor>();
builder.Services.AddHttpClient<IRiotMatchClient, RiotMatchClient>();
builder.Services.AddHttpClient<IRiotPlatformClient, RiotPlatformClient>();
builder.Services.AddHttpClient<IRiotAccountClient, RiotAccountClient>();

builder.Services.AddScoped<ILadderDiscoveryService, LadderDiscoveryService>();
builder.Services.AddScoped<IAccountUpsertService, AccountUpsertService>();
builder.Services.AddScoped<ICandidateUpsertService, CandidateUpsertService>();

builder.Services.AddScoped<IMatchClaimService, MatchClaimService>();
builder.Services.AddScoped<IMatchSnapshotWriter, MatchSnapshotWriter>();
builder.Services.AddScoped<ITimelineIngestionService, TimelineIngestionService>();
builder.Services.AddScoped<IAccountValidationService, AccountValidationService>();

builder.Services.AddScoped<IMainStatsCalculator, MainStatsCalculator>();
builder.Services.AddScoped<IMainDemotionPolicy, MainDemotionPolicy>();
builder.Services.AddHttpClient<IItemMetadataProvider, CommunityDragonItemMetadataProvider>();

builder.Services.AddScoped<DiscoveryProcess>();
builder.Services.AddScoped<ScoringProcess>();
builder.Services.AddScoped<MatchIngestionProcess>();
builder.Services.AddScoped<MainAnalysisProcess>();
builder.Services.AddScoped<ChampionPatternAggregationProcess>();
builder.Services.AddScoped<AccountRefreshProcess>();
builder.Services.AddScoped<MatchDataRetentionProcess>();
builder.Services.AddSingleton<IProcessRunRecorder, ProcessRunRecorder>();

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

builder.Services.AddSingleton<IDataRepositoryFactory, DataRepositoryFactory>();
builder.Services.AddSingleton<IDataSessionFactory, DataSessionFactory>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
