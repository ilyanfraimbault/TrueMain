using System.Threading.RateLimiting;
using Core.Options;
using Data;
using Data.Logging.Crash;
using Data.Logging.Mongo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Scalar.AspNetCore;
using TrueMain.Authentication;
using TrueMain.Options;
using TrueMain.Services.Champions;
using TrueMain.Services.Ops;
using TrueMain.Services.Truemains;
using AspNetCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

var builder = WebApplication.CreateBuilder(args);
const string frontendCorsPolicy = "FrontendCors";

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
// Bound the shared response cache so a crafted fan-out of distinct request
// shapes (the /truemains leaderboard key includes region/champion/position/
// page) can't grow it without limit. Entries are counted (Size = 1 each, set
// at every call site) rather than weighed by bytes: the growth axis here is
// key cardinality, not payload size, and 1024 distinct live entries sits far
// above any legitimate working set within the 30s TTL.
builder.Services.AddMemoryCache(options => options.SizeLimit = 1024);

var healthConnectionString = builder.Configuration.GetConnectionString("TrueMain");
var healthChecks = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(healthConnectionString))
{
    healthChecks.AddNpgSql(
        healthConnectionString,
        name: "postgres",
        tags: ["ready"]);
}
else if (builder.Environment.IsProduction())
{
    // In Production a missing connection string would silently drop the "ready"
    // check, leaving /readyz green while the app can't reach Postgres. Fail fast
    // at boot instead so a misconfigured deployment never reports ready (all
    // deployments run as Production — see compose*.yaml). Development keeps the
    // soft path so the app still starts before user secrets are wired up, and
    // the integration-test "Testing" host injects the connection string after
    // this point (via ConfigureAppConfiguration), so it must not trip here.
    throw new InvalidOperationException(
        "Missing connection string. Add ConnectionStrings:TrueMain so the Postgres "
        + "readiness health check can be registered in Production.");
}

// CORS origins must be present outside Development: an empty list still builds a
// valid (but no-op) policy, so without this guard production silently ships a
// CORS policy that allows no cross-origin browser request — the frontend appears
// to work locally (Development ships real origins) but breaks in prod, where
// appsettings.json ships an empty array. Fail the boot when empty in any
// non-Development environment; only warn under Development (handled after build).
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddOptions<FrontendCorsOptions>()
    .Bind(builder.Configuration.GetSection(FrontendCorsOptions.SectionName))
    .Validate(
        options => isDevelopment || options.Origins.Length > 0,
        "Cors:Origins must contain at least one origin outside the Development environment; "
        + "an empty list ships a no-op CORS policy that silently rejects the frontend.")
    .ValidateOnStart();
builder.Services.AddCors();
// Build the FrontendCors policy from the bound FrontendCorsOptions (single
// source — no separate config read) so the validated origins are the ones the
// policy uses.
builder.Services.AddOptions<AspNetCorsOptions>()
    .Configure<IOptions<FrontendCorsOptions>>((corsPolicies, appCors) =>
        corsPolicies.AddPolicy(frontendCorsPolicy, policy =>
        {
            var builderPolicy = policy.AllowAnyHeader().AllowAnyMethod();
            // Origins is guaranteed non-empty outside Development by
            // ValidateOnStart; this guard only matters under Development, where an
            // empty list is tolerated (and the policy then allows no origin).
            if (appCors.Value.Origins.Length > 0)
            {
                builderPolicy.WithOrigins(appCors.Value.Origins);
            }
        }));

builder.Services.AddOptions<MainAnalysisOptions>()
    .Bind(builder.Configuration.GetSection("MainAnalysis"))
    .Validate(options => Enum.IsDefined(options.QueueId), "MainAnalysis:QueueId must be a defined LolQueueId.")
    .ValidateOnStart();
builder.Services.AddOptions<OpsOptions>()
    .Bind(builder.Configuration.GetSection("Ops"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<TruemainsLeaderboardOptions>()
    .Bind(builder.Configuration.GetSection(TruemainsLeaderboardOptions.SectionName))
    // MinRankedGames is compared against main_champion_stats.TotalMatches,
    // which saturates at MainAnalysis.MatchesToConsider — so the real upper
    // bound is that option, not a constant. Cross-validate the two instead of
    // hard-coding 50: above the cap the TotalMatches predicate could never
    // match and the leaderboard would silently empty out.
    .Validate<IOptions<MainAnalysisOptions>>(
        (leaderboard, mainAnalysis) =>
        {
            var cap = mainAnalysis.Value.MatchesToConsider;
            if (leaderboard.MinRankedGames >= 0 && leaderboard.MinRankedGames <= cap)
            {
                return true;
            }

            // Validate's failure message is a static string; throw so the boot
            // log names the actual values instead of a generic range.
            throw new OptionsValidationException(
                TruemainsLeaderboardOptions.SectionName,
                typeof(TruemainsLeaderboardOptions),
                [
                    $"TruemainsLeaderboard:MinRankedGames ({leaderboard.MinRankedGames}) must be "
                    + $"between 0 and MainAnalysis:MatchesToConsider ({cap})."
                ]);
        },
        "TruemainsLeaderboard:MinRankedGames is out of range.")
    .ValidateOnStart();
builder.Services.AddOptions<ChampionsListOptions>()
    .Bind(builder.Configuration.GetSection(ChampionsListOptions.SectionName))
    .Validate(options => options.MinSampleGames >= 0, "ChampionsList:MinSampleGames must be >= 0.")
    .Validate(options => options.MinMatchupGames >= 0, "ChampionsList:MinMatchupGames must be >= 0.")
    .Validate(options => options.MinPlayerMatchupGames >= 0, "ChampionsList:MinPlayerMatchupGames must be >= 0.")
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization();

// Rate limiting: default per-IP fixed window (100 req / min with a small
// queue) shields the public champion endpoints from casual abuse. Ops
// endpoints are already gated by ApiKey and don't need the same ceiling —
// they opt into a dedicated named policy ("ops") which is attached via
// [EnableRateLimiting("ops")] on the controller if we ever want it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});
builder.Services.AddScoped<IChampionSummariesQueryService, ChampionSummariesQueryService>();
builder.Services.AddScoped<IChampionTierListQueryService, ChampionTierListQueryService>();
builder.Services.AddScoped<IChampionBuildsQueryService, ChampionBuildsQueryService>();
builder.Services.AddScoped<IChampionMatchupQueryService, ChampionMatchupQueryService>();
builder.Services.AddScoped<IChampionTimelineLeadsQueryService, ChampionTimelineLeadsQueryService>();
builder.Services.AddScoped<IChampionScalingQueryService, ChampionScalingQueryService>();
builder.Services.AddScoped<IChampionItemTimingsQueryService, ChampionItemTimingsQueryService>();
builder.Services.AddScoped<IChampionRoamQueryService, ChampionRoamQueryService>();
builder.Services.AddScoped<IChampionPowerspikesQueryService, ChampionPowerspikesQueryService>();
builder.Services.AddScoped<IChampionTrendQueryService, ChampionTrendQueryService>();
builder.Services.AddScoped<IChampionPatchDiffQueryService, ChampionPatchDiffQueryService>();
builder.Services.AddScoped<IMatchSummariesQueryService, MatchSummariesQueryService>();
builder.Services.AddScoped<IMatchDetailQueryService, MatchDetailQueryService>();
builder.Services.AddScoped<IProfileQueryService, ProfileQueryService>();
builder.Services.AddScoped<IPlayerChampionBuildsQueryService, PlayerChampionBuildsQueryService>();
builder.Services.AddScoped<IPlayerChampionMatchupQueryService, PlayerChampionMatchupQueryService>();
builder.Services.AddScoped<IRankHistoryQueryService, RankHistoryQueryService>();
builder.Services.AddScoped<ITruemainsLeaderboardQueryService, TruemainsLeaderboardQueryService>();
builder.Services.AddScoped<ISearchQueryService, SearchQueryService>();
builder.Services.AddScoped<IPipelineHealthQueryService, PipelineHealthQueryService>();
builder.Services.AddScoped<IOverviewQueryService, OverviewQueryService>();
builder.Services.AddScoped<IChampionStatsQueryService, ChampionStatsQueryService>();
builder.Services.AddScoped<IMatchesOverTimeQueryService, MatchesOverTimeQueryService>();
builder.Services.AddScoped<ITableStatsQueryService, TableStatsQueryService>();
builder.Services.AddScoped<IProcessRunsQueryService, ProcessRunsQueryService>();
builder.Services.AddScoped<IProcessIterationsQueryService, ProcessIterationsQueryService>();
builder.Services.AddScoped<ILogsQueryService, LogsQueryService>();
builder.Services.AddScoped<ICrashesQueryService, CrashesQueryService>();
builder.Services.AddScoped<IRiotApiUsageQueryService, RiotApiUsageQueryService>();
builder.Services.AddScoped<IDataQualityQueryService, DataQualityQueryService>();
builder.Services.AddScoped<ISeedRequestService, SeedRequestService>();
builder.Services.AddScoped<ISeedRequestQueryService, SeedRequestQueryService>();
builder.Services.AddScoped<ICandidateQueryService, CandidateQueryService>();
builder.Services.AddDbContext<TrueMainDbContext>(options =>
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
// IDbContextFactory lets services that fire concurrent queries (e.g.
// ProfileQueryService) create short-lived, independently owned contexts per
// parallel branch. No options lambda on purpose: AddDbContext above already
// registered DbContextOptions<TrueMainDbContext> (via TryAdd), so the factory
// reuses that exact registration — same NpgsqlDataSource, connection pool and
// EF model. Passing an options lambda here would be dead code (its own TryAdd
// is a no-op once AddDbContext has run) and would risk silently building a
// second data source if the two registrations were ever reordered. The Scoped
// lifetime matches AddDbContext's options lifetime and leaves the scoped
// TrueMainDbContext registration untouched.
builder.Services.AddDbContextFactory<TrueMainDbContext>(lifetime: ServiceLifetime.Scoped);

// Persist Warning+ logs to MongoDB (see Data/Logging/Mongo) so the /ops/logs
// admin endpoint can serve them, and expose the lossless operator-action audit
// writer (IAuditLog) used by the seed flow. The diagnostic sink drains a bounded
// channel on a background service and never blocks request threads; the audit
// writer inserts synchronously. ProcessName "Api" tags diagnostic rows apart from
// the Ingestor's.
builder.Services.AddMongoLogging(builder.Configuration, processName: "Api");
// Durable crash capture (file first, then Mongo) layered on the Mongo logging it
// depends on, so a crash is recorded even when the restart:unless-stopped policy
// would otherwise hide it — and even if Mongo itself is down.
builder.Services.AddCrashReporting();
var app = builder.Build();

// Wire the process-level crash hooks (AppDomain / TaskScheduler) and the
// unclean-shutdown sentinel before anything runs, so a fault during startup or on a
// background thread is still captured.
app.Services.UseProcessCrashCapture();

// Non-Development boots already fail in ValidateOnStart when Origins is empty;
// this only fires under Development, where an empty list is tolerated but still
// worth flagging so a missing local override doesn't read as a working CORS setup.
if (app.Environment.IsDevelopment()
    && app.Services.GetRequiredService<IOptions<FrontendCorsOptions>>().Value.Origins.Length == 0)
{
    app.Logger.LogWarning(
        "Cors:Origins is empty; the {Policy} policy allows no cross-origin browser request. Set Cors:Origins in configuration to let the frontend reach the API.",
        frontendCorsPolicy);
}

// Wrap unhandled exceptions in RFC 7807 ProblemDetails so clients
// always see a structured payload instead of HTML stack traces, and
// emit StatusCodePages for 4xx/5xx responses without a body so things
// like a bare 404 still arrive as ProblemDetails JSON.
app.UseExceptionHandler();
app.UseStatusCodePages();

// The OpenAPI JSON document (default /openapi/v1.json) and the Scalar UI
// at /scalar/v1 are served only in Development so no API surface metadata
// is exposed in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// HSTS instructs browsers to only reach the API over HTTPS. Skip it in
// Development (localhost is typically HTTP and a cached HSTS policy would
// wedge local debugging); enable it everywhere else, ahead of the HTTPS
// redirect, matching the canonical ASP.NET Core middleware order behind TLS.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors(frontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).DisableRateLimiting();
app.MapHealthChecks("/readyz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).DisableRateLimiting();
app.MapControllers();

// Resolved before Run: a failed start disposes the provider, so resolving inside the
// catch would throw ObjectDisposedException and mask the real fault.
var crashReporter = app.Services.GetRequiredService<ICrashReporter>();
try
{
    await DatabaseMigrator.ApplyPendingMigrationsAsync(app.Services);
    app.Run();
}
catch (Microsoft.Extensions.Hosting.HostAbortedException)
{
    // Deliberate host abort (WebApplicationFactory / EF tooling), not a crash.
    throw;
}
catch (Exception ex)
{
    try { crashReporter.Report(CrashSource.HostRun, ex); }
    catch { /* never let crash reporting mask the original failure */ }
    throw;
}

public partial class Program;
