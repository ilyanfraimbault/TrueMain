using Core.Options;
using Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrueMain.Authentication;
using TrueMain.Options;
using TrueMain.Services.Champions;
using TrueMain.Services.Ops;

var builder = WebApplication.CreateBuilder(args);
const string frontendCorsPolicy = "FrontendCors";

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
builder.Services.AddOptions<MigrationsOptions>()
    .Bind(builder.Configuration.GetSection("Migrations"));

builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IChampionFoundationQueryService, ChampionFoundationQueryService>();
builder.Services.AddScoped<IChampionBuildTreeQueryService, ChampionBuildTreeQueryService>();
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
var app = builder.Build();
var migrationsOptions = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<MigrationsOptions>>()
    .Value;

if (migrationsOptions.ApplyOnStartup)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TrueMainDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/swagger/v1/swagger.json"),
    branch => branch.Use(async (ctx, next) =>
    {
        var authResult = await ctx.AuthenticateAsync(ApiKeyAuthenticationDefaults.Scheme);
        if (!authResult.Succeeded)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next();
    }));

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors(frontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/readyz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapControllers();

app.Run();

public partial class Program;
