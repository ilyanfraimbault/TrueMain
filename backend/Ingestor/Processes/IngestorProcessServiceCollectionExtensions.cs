using System.Reflection;
using Data.Pipeline;
using Ingestor.Processes.Components.ManualSeed;
using Ingestor.Processes.Components.MatchIngestion;

namespace Ingestor.Processes;

public static class IngestorProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the process of every <see cref="PipelineCatalog"/> step, found by its
    /// <see cref="PipelineStepAttribute"/>, keyed by the step's <see cref="JobMode"/> and wrapped
    /// in <see cref="RecordedProcess{TInner}"/> so every run is recorded; the
    /// <see cref="ProcessRunner"/> that runs them; and the steps' boot recoveries.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A catalog step has no process, two processes claim one step, or a process claims a mode
    /// the catalog does not declare.
    /// </exception>
    public static IServiceCollection AddIngestorProcesses(this IServiceCollection services)
    {
        foreach (var (step, processType) in ProcessTypes(typeof(IngestorProcessServiceCollectionExtensions).Assembly))
        {
            services.AddRecordedProcess(processType, step);
        }

        services.AddSingleton<ProcessRunner>();
        services.AddScoped<IStepBootRecovery, MatchClaimBootRecovery>();
        services.AddScoped<IStepBootRecovery, SeedClaimBootRecovery>();
        return services;
    }

    internal static IReadOnlyList<(PipelineProcess Step, Type ProcessType)> ProcessTypes(Assembly assembly)
    {
        var claimed = assembly.GetTypes()
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<PipelineStepAttribute>()))
            .Where(candidate => candidate.Attribute is not null)
            .ToList();

        foreach (var (type, attribute) in claimed)
        {
            if (!typeof(IIngestorProcess).IsAssignableFrom(type) || type.IsAbstract)
            {
                throw new InvalidOperationException($"{type.Name} carries [PipelineStep] but is not a concrete {nameof(IIngestorProcess)}.");
            }

            if (PipelineCatalog.CompositeModes.Contains(attribute!.Mode) || !Enum.IsDefined(attribute.Mode))
            {
                throw new InvalidOperationException($"{type.Name} claims {attribute.Mode}, which is not a single pipeline step.");
            }
        }

        return PipelineCatalog.Steps
            .Select(step =>
            {
                var implementations = claimed.Where(candidate => candidate.Attribute!.Mode == step.Mode).ToList();
                return implementations.Count == 1
                    ? (step, implementations[0].Type)
                    : throw new InvalidOperationException(
                        $"Pipeline step {step.Name} ({step.Mode}) needs exactly one [PipelineStep] process; found {implementations.Count}.");
            })
            .ToList();
    }
}
