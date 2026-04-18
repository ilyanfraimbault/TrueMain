using Ingestor.Services;

namespace Ingestor.Processes;

public static class RecordedProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the concrete <typeparamref name="TProcess"/> as Scoped, plus a
    /// scoped <see cref="IIngestorProcess"/> registration that wraps it in
    /// <see cref="RecordedProcess{TInner}"/>. The wrapper is resolved later via
    /// <c>IEnumerable&lt;IIngestorProcess&gt;</c> and indexed by <c>Name</c>;
    /// there is no .NET keyed-DI involved.
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
