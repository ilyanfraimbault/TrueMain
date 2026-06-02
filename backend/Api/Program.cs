using System.Threading.RateLimiting;
using Core.Options;
using Data;
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

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy =>
    {
        var builderPolicy = policy.AllowAnyHeader().AllowAnyMethod();
        if (corsOrigins.Length > 0)
        {
            builderPolicy.WithOrigins(corsOrigins);
        }
    });
});

builder.Services.AddOptions<MainAnalysisOptions>()
    .Bind(builder.Configuration.GetSection("MainAnalysis"))
    .Validate(options => options.QueueId > 0, "MainAnalysis:QueueId must be greater than 0.")
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
            leaderboard.MinRankedGames >= 0
            && leaderboard.MinRankedGames <= mainAnalysis.Value.MatchesToConsider,
        "TruemainsLeaderboard:MinRankedGames must be between 0 and MainAnalysis:MatchesToConsider.")
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
builder.Services.AddScoped<IChampionBuildsQueryService, ChampionBuildsQueryService>();
builder.Services.AddScoped<IMatchSummariesQueryService, MatchSummariesQueryService>();
builder.Services.AddScoped<IProfileQueryService, ProfileQueryService>();
builder.Services.AddScoped<IRankHistoryQueryService, RankHistoryQueryService>();
builder.Services.AddScoped<ITruemainsLeaderboardQueryService, TruemainsLeaderboardQueryService>();
builder.Services.AddScoped<IPipelineHealthQueryService, PipelineHealthQueryService>();
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
var app = builder.Build();
await DatabaseMigrator.ApplyPendingMigrationsAsync(app.Services);

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

app.Run();

public partial class Program;
