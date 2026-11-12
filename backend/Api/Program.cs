using Data.Logging.Crash;
using Scalar.AspNetCore;
using TrueMain.Authentication;
using TrueMain.Configuration;
using TrueMain.HealthChecks;
using TrueMain.Http;
using TrueMain.LogIngest;
using TrueMain.OpenApi;
using TrueMain.RateLimiting;
using TrueMain.RequestLatency;
using TrueMain.RequestLogging;
using TrueMain.Services;
using TrueMain.Services.Benchmarks;
using TrueMain.Services.Champions;
using TrueMain.Services.Champions.Warmup;
using TrueMain.Services.Desktop;
using TrueMain.Services.Ops;
using TrueMain.Services.Personas;
using TrueMain.Services.Truemains;

var builder = WebApplication.CreateBuilder(args);

// A read backing off after a recent failure answers 503 + Retry-After, not a generic 500.
builder.Services.AddControllers(options => options.Filters.Add<ReadBackoffExceptionFilter>());
builder.Services.AddProblemDetails(options =>
    // Every ProblemDetails response carries the traceId, so a user-reported error can be matched
    // to server logs without embedding entity ids in the message.
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddTrueMainContract();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTrueMainApiCache();
builder.Services.AddTrueMainHealthChecks(builder.Configuration, builder.Environment);
builder.Services.AddTrueMainCors(builder.Environment);

builder.Services.AddTrueMainOpsAuthentication();
builder.Services.AddTrueMainRateLimiting();
builder.Services.AddTrueMainLogIngest();
builder.Services.AddTrueMainRequestLatency();

builder.Services.AddTrueMainChampions(builder.Configuration);
builder.Services.AddTrueMainBenchmarks();
builder.Services.AddTrueMainTruemains();
builder.Services.AddTrueMainPersonas();
builder.Services.AddTrueMainDesktopTelemetry();
builder.Services.AddTrueMainOps();
builder.Services.AddTrueMainStores(builder.Configuration);
var app = builder.Build();

// Before anything runs, so a fault during startup or on a background thread is still captured.
app.Services.UseProcessCrashCapture();

// Outermost, ahead of the exception handler: logs 5xx answers and client aborts with their path
// and traceId (#1555); then the ops-route timer (#2307).
app.UseRequestOutcomeLogging();
app.UseOpsRequestLatency();

// Development gets the debug page; everywhere else answers RFC 7807 ProblemDetails, and
// StatusCodePages turns a bodiless 4xx/5xx into one too.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

// Not in Development: localhost is typically HTTP and a cached HSTS policy would wedge local debugging.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirectionWhenServed();

// The OpenAPI document and the Scalar UI are served in Development only.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseTrueMainCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseChampionWarmupRequests();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapControllers();

// Resolved before Run: a failed start disposes the provider, so resolving inside the
// catch would throw ObjectDisposedException and mask the real fault.
var crashReporter = app.Services.GetRequiredService<ICrashReporter>();
try
{
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
