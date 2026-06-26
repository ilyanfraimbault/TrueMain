using Data;
using Data.Logging.Crash;
using Data.Logging.Mongo;
using Data.Repositories;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Components.MainAnalysis;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddValidatedOptions(builder.Configuration);
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

// RiotApiMetricsHandler is added AFTER the resilience handler so it sits inside
// it in the pipeline: it then records every physical attempt, including the
// retried 429s the resilience handler backs off on — which is what the
// /ops/riot-usage rate-limit and status-code views need (#93).
builder.Services.AddTransient<RiotApiMetricsHandler>();
builder.Services.AddHttpClient<IRiotMatchClient, RiotMatchClient>(ConfigureRiotClient)
    .AddRiotResilienceHandler().AddHttpMessageHandler<RiotApiMetricsHandler>();
builder.Services.AddHttpClient<IRiotPlatformClient, RiotPlatformClient>(ConfigureRiotClient)
    .AddRiotResilienceHandler().AddHttpMessageHandler<RiotApiMetricsHandler>();
builder.Services.AddHttpClient<IRiotAccountClient, RiotAccountClient>(ConfigureRiotClient)
    .AddRiotResilienceHandler().AddHttpMessageHandler<RiotApiMetricsHandler>();

// Single clock source for the whole ingestor: every process and component that
// needs "now" injects TimeProvider and calls GetUtcNow().UtcDateTime instead of
// DateTime.UtcNow, so time-dependent business logic can be frozen under test (#270).
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<ILadderDiscoveryService, LadderDiscoveryService>();
builder.Services.AddScoped<IAccountUpsertService, AccountUpsertService>();
builder.Services.AddScoped<ICandidateUpsertService, CandidateUpsertService>();
builder.Services.AddScoped<IParticipantHarvestService, ParticipantHarvestService>();
builder.Services.AddScoped<IRankSnapshotWriter, RankSnapshotWriter>();

builder.Services.AddScoped<IMatchClaimService, MatchClaimService>();
builder.Services.AddScoped<IMatchSnapshotWriter, MatchSnapshotWriter>();
builder.Services.AddScoped<ITimelineIngestionService, TimelineIngestionService>();
builder.Services.AddScoped<IAccountValidationService, AccountValidationService>();

builder.Services.AddScoped<IMainStatsCalculator, MainStatsCalculator>();
builder.Services.AddScoped<IMainDemotionPolicy, MainDemotionPolicy>();
builder.Services.AddScoped<IChampionCoverageProvider, ChampionCoverageProvider>();
builder.Services.AddHttpClient<IItemMetadataProvider, CommunityDragonItemMetadataProvider>();
builder.Services.AddScoped<ChampionPatternSourceRowReader>();
builder.Services.AddScoped<ChampionPatternAggregateBuilder>();
builder.Services.AddScoped<ChampionPatternAggregatePersister>();
builder.Services.AddScoped<IChampionDimensionResolver, ChampionDimensionResolver>();

// The iteration id is set by the Worker per pass and read by the recorder, both
// singletons; the AsyncLocal inside keeps the value isolated to each pass's flow.
builder.Services.AddSingleton<IIterationContext, IterationContext>();
builder.Services.AddSingleton<IProcessRunRecorder, ProcessRunRecorder>();
builder.Services.AddRecordedProcess<DiscoveryProcess>();
builder.Services.AddRecordedProcess<ManualSeedProcess>();
builder.Services.AddRecordedProcess<HarvestProcess>();
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

// Persist Warning+ logs to MongoDB (see Data/Logging/Mongo). This is what makes
// Ingestor process failures queryable from /ops/logs: a failed run is logged via
// ILogger.LogError in Worker.RunOnceAsync, which now flows through this provider.
// Also exposes the lossless IAuditLog used by ManualSeedProcess. ProcessName
// "Ingestor" tags diagnostic rows apart from the API's.
builder.Services.AddMongoLogging(builder.Configuration, processName: "Ingestor");
// Durable crash capture (file first, then Mongo) layered on the Mongo logging it
// depends on. This is what makes a silent ingestor crash visible: a fault that
// escapes the worker, or an OOM/SIGKILL the restart policy hides, leaves a record.
builder.Services.AddCrashReporting();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Wire the process-level crash hooks and the unclean-shutdown sentinel before the
// host runs, so a startup/migration fault or a background-thread throw is captured.
host.Services.UseProcessCrashCapture();

// Resolved before Run: a failed start disposes the provider, so resolving inside the
// catch would throw ObjectDisposedException and mask the real fault.
var crashReporter = host.Services.GetRequiredService<ICrashReporter>();
await DatabaseMigrator.ApplyPendingMigrationsAsync(host.Services);
try
{
    await host.RunAsync();
}
catch (Microsoft.Extensions.Hosting.HostAbortedException)
{
    throw;
}
catch (Exception ex)
{
    try { crashReporter.Report(CrashSource.HostRun, ex); }
    catch { /* never let crash reporting mask the original failure */ }
    throw;
}

static void ConfigureRiotClient(IServiceProvider serviceProvider, HttpClient client)
{
    var options = serviceProvider.GetRequiredService<IOptions<RiotOptions>>().Value;
    client.DefaultRequestHeaders.Add("X-Riot-Token", options.ApiKey);
}
