using Data.Pipeline;

namespace Ingestor.Processes;

public static class RecordedProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TProcess"/> as Scoped, plus a scoped
    /// <see cref="IIngestorProcess"/> wrapped in <see cref="RecordedProcess{TInner}"/> and keyed
    /// by <paramref name="step"/>'s <see cref="JobMode"/>.
    /// </summary>
    public static IServiceCollection AddRecordedProcess<TProcess>(this IServiceCollection services, PipelineProcess step)
        where TProcess : class, IIngestorProcess
        => services.AddRecordedProcess(typeof(TProcess), step);

    internal static IServiceCollection AddRecordedProcess(this IServiceCollection services, Type processType, PipelineProcess step)
    {
        var recordedType = typeof(RecordedProcess<>).MakeGenericType(processType);
        services.AddScoped(processType);
        services.AddKeyedScoped<IIngestorProcess>(step.Mode, (sp, _) =>
            (IIngestorProcess)ActivatorUtilities.CreateInstance(sp, recordedType, sp.GetRequiredService(processType), step));
        return services;
    }
}
