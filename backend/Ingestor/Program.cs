using Data;
using Data.Configuration;
using Data.Logging.Crash;
using Data.Logging.Mongo;
using Data.Repositories;
using Data.Statics;
using Ingestor;
using Ingestor.BuildFacts;
using Ingestor.Configuration;
using Ingestor.FetchWorkers;
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
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddValidatedOptions(builder.Configuration);
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

// The three typed Riot clients on the one shared pipeline: limiter, resilience, observer,
// metrics (RiotClientServiceCollectionExtensions says why in that order).
builder.Services.AddRiotClient<IRiotMatchClient, RiotMatchClient>();
builder.Services.AddRiotClient<IRiotPlatformClient, RiotPlatformClient>();
builder.Services.AddRiotClient<IRiotAccountClient, RiotAccountClient>();

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
builder.Services.AddSingleton<MatchFetchWorkerPool>();
// One brake for every worker, so a backpressure transition is announced once (#1459).
builder.Services.AddSingleton<IHostResourceProbe, HostResourceProbe>();
builder.Services.AddSingleton<IngestionBackpressure>();

builder.Services.AddScoped<IMainStatsCalculator, MainStatsCalculator>();
builder.Services.AddScoped<IMainDemotionPolicy, MainDemotionPolicy>();
builder.Services.AddScoped<IChampionCoverageProvider, ChampionCoverageProvider>();
// Static game data (CommunityDragon item metadata, Data Dragon champion statics and item
// stats) rides one client behind the standard resilience handler (#251, #1449, #1750):
// community-run or static-file CDNs, flakier than the Riot API and late on patch day, with
// no Retry-After semantics. The providers load with CancellationToken.None (the in-flight
// task is shared), so the pipeline's total timeout — and the client timeout above it — are
// the only bounds on a load.
builder.Services
    .AddHttpClient(StaticGameDataServiceCollectionExtensions.ClientName, ConfigureCommunityDragonClient)
    .AcceptCompressedResponses()
    .AddCommunityDragonResilienceHandler();
builder.Services.AddStaticGameData(provider => provider.GetRequiredService<IHttpClientFactory>()
    .CreateClient(StaticGameDataServiceCollectionExtensions.ClientName));
builder.Services.AddScoped<ChampionPatternSourceRowReader>();
builder.Services.AddScoped<ChampionPatternAggregateBuilder>();
builder.Services.AddScoped<ChampionPatternAggregatePersister>();
builder.Services.AddScoped<IChampionDimensionResolver, ChampionDimensionResolver>();

// The iteration id is set by the Worker per pass and read by the recorder, both
// singletons; the AsyncLocal inside keeps the value isolated to each pass's flow.
builder.Services.AddSingleton<IIterationContext, IterationContext>();
builder.Services.AddSingleton<IProcessRunRecorder, ProcessRunRecorder>();
// Same shape as IIterationContext, but carries the running process's name for
// RiotApiMetricsHandler to attribute Riot API consumption per caller (#1035).
builder.Services.AddSingleton<ICallerContext, CallerContext>();
builder.Services.AddIngestorProcesses();

builder.Services.AddTrueMainData(builder.Configuration);
// Startup migrations run as the first hosted service (#258), so they finish before the
// Worker — registered last, below — touches the schema. Gated on
// Database:ApplyMigrationsOnStartup, which prod and preprod keep disabled.
builder.Services.AddDatabaseMigrationsOnStartup();

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
// Publishes this process's bound options to Mongo once at boot (#1034), so the admin
// configuration page can show what the Ingestor is actually running with — the Api
// cannot introspect this container's options classes, so this is the only side that
// can report them. Registered ahead of the Worker below: hosted services start in
// registration order, and the Worker can end the process after its first pass.
builder.Services.AddEffectiveConfigurationPublisher(IngestorEffectiveConfigurationCatalog.Instance);
// Appends this boot's version to the boot log the admin deploy markers read (#2297), keyed by
// the job mode because the fetch and aggregate lanes are two containers of one image.
builder.Services.AddProcessBootRecorder(
    services => JobModeParser.Parse(services.GetRequiredService<IOptions<JobOptions>>().Value.Mode).ToString());
// Metrics pipeline: AddMetrics registers the IMeterFactory that IngestorMetrics builds
// the "TrueMain.Ingestor" meter from, so the meter's lifetime is the host's instead of a
// process-wide static. It is idempotent (TryAdd), so calling it explicitly here is safe
// and keeps the registration visible next to its consumer.
builder.Services.AddMetrics();
builder.Services.AddSingleton<IngestorMetrics>();
// Folds that meter into per-minute Mongo rollups the admin reads (#1636). Ahead of the
// Worker so it stops after it, and the final flush holds the last pass's measurements.
builder.Services.AddHostedService<IngestorMeterExporter>();

// Resolved once at construction from INGESTOR_HEARTBEAT_PATH, rather than read from the
// environment on every beat: the environment is process-global and a worker is not (#1348).
builder.Services.AddSingleton<IHeartbeatFile, EnvironmentHeartbeatFile>();

// A redeploy stops the container, and until #1513 the stop was a 10-second SIGTERM →
// SIGKILL: Docker's default grace period, which nothing overrode. The work is cooperative
// — stoppingToken is threaded through every process and every write is transactional — so
// what it lacked was the time to unwind, record the cancelled run and let the in-flight
// transaction settle. The compose files now grant a 120 s stop grace period; this budget
// sits below it so the host finishes on its own terms rather than being killed halfway
// through the finalisation. Only a ceiling: a cancelled pass normally stops in seconds.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(100));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Wire the process-level crash hooks and the unclean-shutdown sentinel before the
// host runs, so a startup fault (a failed migration included) or a background-thread
// throw is captured.
host.Services.UseProcessCrashCapture();

// Resolved before Run: a failed start disposes the provider, so resolving inside the
// catch would throw ObjectDisposedException and mask the real fault.
var crashReporter = host.Services.GetRequiredService<ICrashReporter>();
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

static void ConfigureCommunityDragonClient(IServiceProvider serviceProvider, HttpClient client)
{
    var options = serviceProvider.GetRequiredService<IOptions<CommunityDragonOptions>>().Value;

    // HttpClient.Timeout wraps the whole resilience pipeline, and its 100s default would
    // silently cut a longer pipeline short — surfacing an opaque TaskCanceledException
    // instead of the handler's timeout. Sizing it just above the configured total keeps
    // the pipeline the single authority on how long a metadata fetch may take. The
    // handler never raises the total above this value (it clamps the per-attempt timeout
    // instead), so this margin always holds.
    client.Timeout = TimeSpan.FromSeconds(options.TotalRequestTimeoutSeconds + 5);
}
