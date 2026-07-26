using Ingestor.Options;
using Ingestor.Services;

namespace Ingestor.Processes;

public static class RecordedProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the concrete <typeparamref name="TProcess"/> as Scoped, plus a
    /// scoped <see cref="IIngestorProcess"/> registration — wrapped in
    /// <see cref="RecordedProcess{TInner}"/> — keyed by the
    /// <paramref name="mode"/> it implements. The worker resolves it with
    /// <c>GetKeyedService&lt;IIngestorProcess&gt;(mode)</c> from the per-process
    /// scope, so the lookup is driven by the <see cref="JobMode"/> enum instead
    /// of a typo-prone process-name string.
    /// </summary>
    public static IServiceCollection AddRecordedProcess<TProcess>(this IServiceCollection services, JobMode mode)
        where TProcess : class, IIngestorProcess
    {
        services.AddScoped<TProcess>();
        services.AddKeyedScoped<IIngestorProcess>(mode, (sp, _) =>
            new RecordedProcess<TProcess>(
                sp.GetRequiredService<TProcess>(),
                sp.GetRequiredService<IProcessRunRecorder>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<RecordedProcess<TProcess>>>()));
        return services;
    }
}
