using Ingestor.Services;

namespace Ingestor.Processes;

public static class RecordedProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the concrete <typeparamref name="TProcess"/> as Scoped, plus a
    /// keyed <see cref="IIngestorProcess"/> instance keyed by the process name
    /// that wraps it in <see cref="RecordedProcess{TInner}"/>.
    /// </summary>
    public static IServiceCollection AddRecordedProcess<TProcess>(this IServiceCollection services)
        where TProcess : class, IIngestorProcess
    {
        services.AddScoped<TProcess>();
        services.AddScoped<IIngestorProcess, RecordedProcess<TProcess>>(sp =>
            new RecordedProcess<TProcess>(
                sp.GetRequiredService<TProcess>(),
                sp.GetRequiredService<IProcessRunRecorder>()));
        return services;
    }
}
