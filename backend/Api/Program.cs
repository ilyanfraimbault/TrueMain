using System.Threading.RateLimiting;
using Core.Options;
using Data;
using Data.BuildFacts;
using Data.Logging.Crash;
using Data.Logging.Mongo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
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
builder.Services.AddProblemDetails(options =>
{
    // Every ProblemDetails response — validation, not-found, or unhandled 5xx —
    // carries the same traceId so a user-reported error can be matched to server
    // logs without ever needing to embed raw entity ids in the message text.
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
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
    // The API feeds PlayRateFloor into the dedication score as the point
    // commitment reads 0 (#869), so it must be range-checked here too and not
    // only in the ingestor: a floor at or above 1 would invert the rescale.
    // Same predicates as the ingestor's (#930 review — the two used to disagree
    // at the upper bound; both hosts now reject exactly 1, since both bind the
    // same MainAnalysis section and DedicationScore.Commitment divides by
    // (1 - floor)).
    .Validate(options => options.PlayRateFloor is >= 0 and < 1, "MainAnalysis:PlayRateFloor must be in [0, 1).")
    .Validate(options => options.PlayRateFloor <= options.PlayRateThreshold,
        "MainAnalysis:PlayRateFloor must be <= PlayRateThreshold.")
    .ValidateOnStart();
builder.Services.AddOptions<OpsOptions>()
    .Bind(builder.Configuration.GetSection("Ops"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<TruemainsLeaderboardOptions>()
    .Bind(builder.Configuration.GetSection(TruemainsLeaderboardOptions.SectionName))
    .ValidateDataAnnotations()
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
    .Validate(options => options.MinBuildSampleGames >= 0, "ChampionsList:MinBuildSampleGames must be >= 0.")
    .Validate(options => options.MinServablePatchLines >= 0, "ChampionsList:MinServablePatchLines must be >= 0.")
    .Validate(options => options.MinMatchupGames >= 0, "ChampionsList:MinMatchupGames must be >= 0.")
    // A share, so out of [0,1) it stops meaning anything: 1 would demand a single
    // opponent account for every game the champion ever played, which no matchup can.
    .Validate(
        options => options.MinMatchupPlayRate is >= 0d and < 1d,
        "ChampionsList:MinMatchupPlayRate must be in [0, 1).")
    .Validate(options => options.MinDecidedLaneGames >= 0, "ChampionsList:MinDecidedLaneGames must be >= 0.")
    // A share, so out of [0,1) it stops meaning anything: 1 would demand a pairing
    // present in every game the champion ever played, which no pairing is.
    .Validate(
        options => options.MinSynergyPlayRate is >= 0d and < 1d,
        "ChampionsList:MinSynergyPlayRate must be in [0, 1).")
    // A share too, but 1 is a meaningful setting here: BaselineSet.IsRealLane
    // divides a champion's games in one lane by its games across all lanes, which
    // is exactly 1 for a mono-lane champion. So [0, 1], closed on both ends.
    .Validate(
        options => options.MinSynergyPartnerLanePlayRate is >= 0d and <= 1d,
        "ChampionsList:MinSynergyPartnerLanePlayRate must be in [0, 1].")
    .Validate(options => options.MinPlayerMatchupGames >= 0, "ChampionsList:MinPlayerMatchupGames must be >= 0.")
    .Validate(options => options.MaxLanesPerChampion >= 0, "ChampionsList:MaxLanesPerChampion must be >= 0.")
    .Validate(
        options => options.MinSecondaryLanePlayRate is >= 0 and <= 1,
        "ChampionsList:MinSecondaryLanePlayRate must be a share between 0 and 1.")
    .ValidateOnStart();
builder.Services.AddOptions<ChampionTierOptions>()
    .Bind(builder.Configuration.GetSection(ChampionTierOptions.SectionName))
    .Validate(options => options.PickRateWeight >= 0, "ChampionTier:PickRateWeight must be >= 0.")
    .Validate(options => options.BanRateWeight >= 0, "ChampionTier:BanRateWeight must be >= 0.")
    .Validate(options => options.WinRateWeight >= 0, "ChampionTier:WinRateWeight must be >= 0.")
    // Must sum to 1 (not just "> 0"): TierScore is documented as a [0,1]
    // blend of percentile-ranked metrics (each already in [0,1]), and the
    // no-ban-data renormalization path (ChampionTierCalculator.ResolveWeights)
    // only preserves that bound if the configured weights start at 1.
    .Validate(
        options => Math.Abs(options.PickRateWeight + options.BanRateWeight + options.WinRateWeight - 1.0) < 1e-9,
        "ChampionTier: PickRateWeight + BanRateWeight + WinRateWeight must sum to 1.")
    .Validate(options => options.WinRateShrinkageGames >= 0, "ChampionTier:WinRateShrinkageGames must be >= 0.")
    .ValidateOnStart();
builder.Services.AddOptions<StorageHistoryOptions>()
    .Bind(builder.Configuration.GetSection(StorageHistoryOptions.SectionName))
    .Validate(options => options.DiskCapacityBytes >= 0, "StorageHistory:DiskCapacityBytes must be >= 0.")
    .Validate(options => options.DefaultWindowDays > 0, "StorageHistory:DefaultWindowDays must be greater than 0.")
    .Validate(options => options.TopTables > 0, "StorageHistory:TopTables must be greater than 0.")
    .Validate(
        options => options.ThresholdPercents.All(percent => percent is > 0 and <= 100),
        "StorageHistory:ThresholdPercents must each be in (0, 100].")
    .ValidateOnStart();
builder.Services.AddOptions<PipelineHealthOptions>()
    .Bind(builder.Configuration.GetSection(PipelineHealthOptions.SectionName))
    // Only enforced when both levels are enabled: either can independently be set to <= 0
    // to disable it (PipelineHealthOptions), and requiring the ordering unconditionally
    // would reject that documented "amber off, red on" configuration at boot.
    .Validate(
        options => options.DiskForecastAmberDays <= 0
            || options.DiskForecastRedDays <= 0
            || options.DiskForecastAmberDays >= options.DiskForecastRedDays,
        "PipelineHealth:DiskForecastAmberDays must be >= DiskForecastRedDays (amber fires first).")
    .ValidateOnStart();
builder.Services.AddOptions<DataQualityDetectorOptions>()
    .Bind(builder.Configuration.GetSection(DataQualityDetectorOptions.SectionName))
    // Only the sizes are validated. The thresholds themselves are deliberately
    // unconstrained: 0 disables a level, and an operator silencing a card by pushing one
    // very high is using the knob as intended, not misconfiguring it.
    .Validate(
        options => options.OrphanSampleMatchesPerPlatform >= 2,
        "DataQualityDetectors:OrphanSampleMatchesPerPlatform must be >= 2 (it is split into two windows).")
    .Validate(
        options => options.FreshnessChampionLimit > 0,
        "DataQualityDetectors:FreshnessChampionLimit must be greater than 0.")
    .Validate(
        options => options.FreshnessPatchCount > 0,
        "DataQualityDetectors:FreshnessPatchCount must be greater than 0.")
    .Validate(
        options => options.PatchVolumeAnomalyRatio is > 0 and < 1,
        "DataQualityDetectors:PatchVolumeAnomalyRatio must be in (0, 1).")
    .Validate(
        options => options.PatchVolumeMinPatches > 0,
        "DataQualityDetectors:PatchVolumeMinPatches must be greater than 0.")
    .ValidateOnStart();
builder.Services.AddOptions<PatchCoverageOptions>()
    .Bind(builder.Configuration.GetSection(PatchCoverageOptions.SectionName))
    .Validate(options => options.PatchCount > 0, "PatchCoverage:PatchCount must be greater than 0.")
    .Validate(options => options.ThinLineLimit > 0, "PatchCoverage:ThinLineLimit must be greater than 0.")
    .Validate(
        options => options.ServableLinesRatio is > 0 and <= 1,
        "PatchCoverage:ServableLinesRatio must be in (0, 1].")
    .Validate(
        options => options.ServableLinesMinimum >= 0,
        "PatchCoverage:ServableLinesMinimum must be >= 0.")
    .ValidateOnStart();
builder.Services.AddOptions<CompositionSearchOptions>()
    .Bind(builder.Configuration.GetSection(CompositionSearchOptions.SectionName))
    .Validate(
        options => options.RoleOpponentWeight >= 0 && options.EnemyWeight >= 0 && options.AllyWeight >= 0,
        "CompositionSearch weights must be >= 0.")
    .Validate(options => options.TopK > 0, "CompositionSearch:TopK must be > 0.")
    .Validate(
        options => options.CandidatePoolCap >= options.TopK,
        "CompositionSearch:CandidatePoolCap must be >= TopK.")
    .Validate(options => options.WinWeight >= 1d, "CompositionSearch:WinWeight must be >= 1.")
    // A negative threshold would make every lane "won" and its mirror "lost" at once.
    .Validate(
        options => options.LaneGoldLeadThreshold >= 0,
        "CompositionSearch:LaneGoldLeadThreshold must be >= 0.")
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization();

// Rate limiting: one global per-IP fixed window (100 req / min with a small
// queue) shields the public champion endpoints from casual abuse. There is no
// separate ops policy — the ops endpoints share this window, and because the
// admin portal proxies them all through one server, they share it from a
// single source IP.
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
// The one door every champion read goes through: shared cache + single flight, keyed
// by the ingestor's aggregation version rather than by a 60s clock (#1368). Registered
// before the reads themselves because every one of them depends on it — a champion
// query service that took a raw IMemoryCache instead would be caching without
// coalescing, which is how a popular page's expiry became ten identical 14s scans.
builder.Services.AddScoped<IChampionAggregationStamp, ChampionAggregationStamp>();
builder.Services.AddScoped<IChampionReadCache, ChampionReadCache>();
builder.Services.AddScoped<IChampionSummariesQueryService, ChampionSummariesQueryService>();
builder.Services.AddScoped<IChampionTierListQueryService, ChampionTierListQueryService>();
builder.Services.AddScoped<IChampionOverviewQueryService, ChampionOverviewQueryService>();
builder.Services.AddScoped<IChampionBuildsQueryService, ChampionBuildsQueryService>();
builder.Services.AddScoped<IChampionMatchupQueryService, ChampionMatchupQueryService>();
builder.Services.AddScoped<IChampionSynergyQueryService, ChampionSynergyQueryService>();
builder.Services.AddScoped<ICompositionMatchQueryService, CompositionMatchQueryService>();
// Shared by the composition recommendation (#921) and the matchup-scoped champion page
// (#923) so a game means the same build to both.
builder.Services.AddScoped<ParticipantBuildFactsLoader>();
builder.Services.AddScoped<ICompositionBuildQueryService, CompositionBuildQueryService>();
builder.Services.AddScoped<IChampionMatchupBuildsQueryService, ChampionMatchupBuildsQueryService>();
builder.Services.AddScoped<ICompositionGamesQueryService, CompositionGamesQueryService>();
builder.Services.AddScoped<ICompositionLaneOutcomeQueryService, CompositionLaneOutcomeQueryService>();
builder.Services.AddScoped<ICompositionRecommendationQueryService, CompositionRecommendationQueryService>();
// Same CommunityDragon item-metadata source as the ingestor's pattern
// aggregation, so the composition recommender reads a game's items
// identically. Patch-cached inside the provider, which clocks how long a
// not-yet-published patch has been served from the fallback branch (#1107).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IItemMetadataProvider, CommunityDragonItemMetadataProvider>();
builder.Services.AddScoped<IChampionScalingQueryService, ChampionScalingQueryService>();
builder.Services.AddScoped<IChampionItemTimingsQueryService, ChampionItemTimingsQueryService>();
builder.Services.AddScoped<IChampionRoamQueryService, ChampionRoamQueryService>();
builder.Services.AddScoped<IChampionPowerspikesQueryService, ChampionPowerspikesQueryService>();
builder.Services.AddScoped<IChampionTrendQueryService, ChampionTrendQueryService>();
builder.Services.AddScoped<IChampionPatchDiffQueryService, ChampionPatchDiffQueryService>();
builder.Services.AddScoped<IChampionMainsComparisonQueryService, ChampionMainsComparisonQueryService>();
// The single name-tag -> account lookup shared by every player-scoped route
// (#1230), so they cannot disagree on which account a Riot ID means.
builder.Services.AddScoped<TruemainAccountResolver>();
// Shared by the truemain match feed and the composition provenance drawer (#940),
// so a game renders as the same row on both.
builder.Services.AddScoped<MatchSummaryHydrator>();
builder.Services.AddScoped<IMatchSummariesQueryService, MatchSummariesQueryService>();
builder.Services.AddScoped<IMatchDetailQueryService, MatchDetailQueryService>();
builder.Services.AddScoped<IProfileQueryService, ProfileQueryService>();
builder.Services.AddScoped<IPlayerChampionBuildsQueryService, PlayerChampionBuildsQueryService>();
builder.Services.AddScoped<IPlayerChampionMatchupQueryService, PlayerChampionMatchupQueryService>();
builder.Services.AddScoped<IPlayerChampionPerformanceQueryService, PlayerChampionPerformanceQueryService>();
builder.Services.AddScoped<IPlayerBuildDivergenceQueryService, PlayerBuildDivergenceQueryService>();
builder.Services.AddScoped<IRankHistoryQueryService, RankHistoryQueryService>();
builder.Services.AddScoped<ITruemainActivityQueryService, TruemainActivityQueryService>();
builder.Services.AddScoped<ITruemainsLeaderboardQueryService, TruemainsLeaderboardQueryService>();
builder.Services.AddScoped<ISearchQueryService, SearchQueryService>();
builder.Services.AddScoped<IPipelineHealthQueryService, PipelineHealthQueryService>();
builder.Services.AddScoped<IOverviewQueryService, OverviewQueryService>();
builder.Services.AddScoped<IChampionStatsQueryService, ChampionStatsQueryService>();
builder.Services.AddScoped<IMatchesOverTimeQueryService, MatchesOverTimeQueryService>();
builder.Services.AddScoped<IMatchesIngestedQueryService, MatchesIngestedQueryService>();
builder.Services.AddScoped<ITableStatsQueryService, TableStatsQueryService>();
builder.Services.AddScoped<IDbStorageHistoryQueryService, DbStorageHistoryQueryService>();
builder.Services.AddScoped<IProcessRunsQueryService, ProcessRunsQueryService>();
builder.Services.AddScoped<IProcessIterationsQueryService, ProcessIterationsQueryService>();
builder.Services.AddScoped<ILogsQueryService, LogsQueryService>();
builder.Services.AddScoped<ICrashesQueryService, CrashesQueryService>();
builder.Services.AddScoped<IRiotApiUsageQueryService, RiotApiUsageQueryService>();
builder.Services.AddScoped<IDataQualityQueryService, DataQualityQueryService>();
builder.Services.AddScoped<IDataQualityDetectorsQueryService, DataQualityDetectorsQueryService>();
builder.Services.AddScoped<IEffectiveConfigurationQueryService, EffectiveConfigurationQueryService>();
builder.Services.AddScoped<ISeedRequestService, SeedRequestService>();
builder.Services.AddScoped<ISeedRequestQueryService, SeedRequestQueryService>();
builder.Services.AddScoped<IAccountExplorerQueryService, AccountExplorerQueryService>();
builder.Services.AddScoped<IAccountFreshnessQueryService, AccountFreshnessQueryService>();
builder.Services.AddScoped<IPatchCoverageQueryService, PatchCoverageQueryService>();
builder.Services.AddScoped<ICandidateQueryService, CandidateQueryService>();
builder.Services.AddScoped<ICandidateFunnelQueryService, CandidateFunnelQueryService>();
builder.Services.AddScoped<ICandidateStockQueryService, CandidateStockQueryService>();
builder.Services.AddScoped<ICandidateQueueLatencyQueryService, CandidateQueueLatencyQueryService>();
builder.Services.AddScoped<IAggregationStatsQueryService, AggregationStatsQueryService>();
// AddTrueMainData registers the IDbContextFactory<TrueMainDbContext> — which
// services that fire concurrent queries (e.g. ProfileQueryService) use to create
// short-lived, independently owned contexts per parallel branch — and, in the
// same call, the scoped TrueMainDbContext for the common request-scoped
// injection. Both share the one NpgsqlDataSource built inside the extension.
builder.Services.AddTrueMainData(builder.Configuration);

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

// Development gets the rich debug page (source snippets, full stack trace);
// everywhere else keeps the RFC 7807 ProblemDetails handler so clients always
// see a structured payload instead of HTML stack traces. StatusCodePages
// covers 4xx/5xx responses without a body so things like a bare 404 still
// arrive as ProblemDetails JSON.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

// HSTS instructs browsers to only reach the API over HTTPS. Skip it in
// Development (localhost is typically HTTP and a cached HSTS policy would
// wedge local debugging); enable it everywhere else, ahead of the HTTPS
// redirect, matching the canonical ASP.NET Core middleware order behind TLS.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// The OpenAPI JSON document (default /openapi/v1.json) and the Scalar UI
// at /scalar/v1 are served only in Development so no API surface metadata
// is exposed in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

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
