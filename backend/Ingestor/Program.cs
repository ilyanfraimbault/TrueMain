using Data;
using Data.Repositories;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Components.MainAnalysis;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddValidatedOptions(builder.Configuration);
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddHttpClient<IRiotMatchClient, RiotMatchClient>().AddRiotResilienceHandler();
builder.Services.AddHttpClient<IRiotPlatformClient, RiotPlatformClient>().AddRiotResilienceHandler();
builder.Services.AddHttpClient<IRiotAccountClient, RiotAccountClient>().AddRiotResilienceHandler();

builder.Services.AddScoped<ILadderDiscoveryService, LadderDiscoveryService>();
builder.Services.AddScoped<IAccountUpsertService, AccountUpsertService>();
builder.Services.AddScoped<ICandidateUpsertService, CandidateUpsertService>();
builder.Services.AddScoped<IRankSnapshotWriter, RankSnapshotWriter>();

builder.Services.AddScoped<IMatchClaimService, MatchClaimService>();
builder.Services.AddScoped<IMatchSnapshotWriter, MatchSnapshotWriter>();
builder.Services.AddScoped<ITimelineIngestionService, TimelineIngestionService>();
builder.Services.AddScoped<IAccountValidationService, AccountValidationService>();

builder.Services.AddScoped<IMainStatsCalculator, MainStatsCalculator>();
builder.Services.AddScoped<IMainDemotionPolicy, MainDemotionPolicy>();
builder.Services.AddHttpClient<IItemMetadataProvider, CommunityDragonItemMetadataProvider>();
builder.Services.AddScoped<ChampionPatternSourceRowReader>();
builder.Services.AddScoped<ChampionPatternAggregateBuilder>();
builder.Services.AddScoped<ChampionPatternAggregatePersister>();
builder.Services.AddScoped<IChampionDimensionResolver, ChampionDimensionResolver>();

builder.Services.AddSingleton<IProcessRunRecorder, ProcessRunRecorder>();
builder.Services.AddRecordedProcess<DiscoveryProcess>();
builder.Services.AddRecordedProcess<ScoringProcess>();
builder.Services.AddRecordedProcess<MatchIngestionProcess>();
builder.Services.AddRecordedProcess<MainAnalysisProcess>();
builder.Services.AddRecordedProcess<ChampionPatternAggregationProcess>();
builder.Services.AddRecordedProcess<AccountRefreshProcess>();
builder.Services.AddRecordedProcess<MatchDataRetentionProcess>();

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
await DatabaseMigrator.ApplyPendingMigrationsAsync(host.Services);
await host.RunAsync();
