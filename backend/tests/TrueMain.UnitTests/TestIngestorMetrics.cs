using System.Diagnostics.Metrics;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TrueMain.UnitTests;

/// <summary>
/// Builds a throwaway <see cref="IngestorMetrics"/> for tests that must satisfy the
/// <c>Worker</c> constructor without asserting on the instruments. Tests that DO assert
/// on them own their own <see cref="IMeterFactory"/> so a <c>MetricCollector</c> can be
/// scoped to it — see <see cref="WorkerFailureMetricsTests"/>.
/// </summary>
internal static class TestIngestorMetrics
{
    // One shared factory for the whole test assembly: meters are cheap, nothing listens
    // to this one, and keeping it alive avoids disposing meters still referenced by a
    // running worker. Disposed with the test process.
    private static readonly IMeterFactory SharedMeterFactory =
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    public static IngestorMetrics Create() => new(SharedMeterFactory);
}
