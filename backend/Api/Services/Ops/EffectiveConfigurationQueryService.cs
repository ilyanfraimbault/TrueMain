using Data.Configuration;
using Data.Ops.Mongo;
using TrueMain.Configuration;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Serves the admin configuration page (#1034) by combining two different kinds of
/// snapshot: the Api's own, built live from its container on every request, and the
/// Ingestor's, published to Mongo at its own boot because the Api cannot introspect a
/// container it does not run in.
/// </summary>
public sealed class EffectiveConfigurationQueryService(
    IServiceProvider services,
    IConfiguration configuration,
    IHostEnvironment environment,
    IEffectiveConfigurationStore store,
    TimeProvider timeProvider) : IEffectiveConfigurationQueryService
{
    public async Task<EffectiveConfigurationOverviewReadModel> GetAsync(CancellationToken ct)
    {
        var apiSnapshot = EffectiveConfigurationBuilder.Build(
            ApiEffectiveConfigurationCatalog.Instance,
            services,
            configuration,
            environment.EnvironmentName,
            timeProvider.GetUtcNow().UtcDateTime);

        var published = await store.GetAllAsync(ct);

        // The Api's live snapshot always wins its own slot: a stale document some past
        // build of this process happened to publish must never shadow what it is
        // actually running right now.
        var processes = new List<EffectiveConfigurationSnapshot> { apiSnapshot };
        processes.AddRange(published.Where(snapshot =>
            !string.Equals(snapshot.ProcessName, apiSnapshot.ProcessName, StringComparison.Ordinal)));

        return new EffectiveConfigurationOverviewReadModel
        {
            Processes = processes
                .OrderBy(snapshot => snapshot.ProcessName, StringComparer.Ordinal)
                .Select(ToReadModel)
                .ToList()
        };
    }

    private static EffectiveConfigurationProcessReadModel ToReadModel(EffectiveConfigurationSnapshot snapshot) =>
        new()
        {
            ProcessName = snapshot.ProcessName,
            Environment = snapshot.Environment,
            Version = snapshot.Version,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            Sections = snapshot.Sections.Select(section => new EffectiveConfigurationSectionReadModel
            {
                Name = section.Name,
                Title = section.Title,
                Description = section.Description,
                Values = section.Values.Select(value => new EffectiveConfigurationValueReadModel
                {
                    Key = value.Key,
                    Name = value.Name,
                    Value = value.Value,
                    ValueLabel = value.ValueLabel,
                    Origin = value.Origin,
                    Source = value.Source,
                    Unit = value.Unit,
                    Notice = value.Notice
                }).ToList()
            }).ToList()
        };
}
