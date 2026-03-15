using Core.Options;
using TrueMain.Services.Champions;
using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddOptions<MainAnalysisOptions>()
    .Bind(builder.Configuration.GetSection("MainAnalysis"))
    .Validate(options => options.QueueId > 0, "MainAnalysis:QueueId must be greater than 0.")
    .ValidateOnStart();
builder.Services.AddScoped<IChampionFoundationQueryService, ChampionFoundationQueryService>();
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
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
