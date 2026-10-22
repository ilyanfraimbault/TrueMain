namespace TrueMain.Services.Benchmarks;

public static class BenchmarksServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reads behind <c>/benchmarks/*</c>, the references the desktop app sets the
    /// player against: the overlay's pace (#1912) and the dashboard's form tiles (#2076).
    /// </summary>
    public static IServiceCollection AddTrueMainBenchmarks(this IServiceCollection services)
    {
        services.AddScoped<IPaceBenchmarkQueryService, PaceBenchmarkQueryService>();
        services.AddScoped<IFormBenchmarkQueryService, FormBenchmarkQueryService>();
        return services;
    }
}
