using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TrueMain.Services.Champions.Warmup;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

/// <summary>
/// The warm-up holds <c>/readyz</c> on <see cref="HealthStatus.Degraded"/> for the whole first
/// pass, and the probes ask several times a minute. The health check service logs every
/// Degraded probe at Warning, which flooded the ops logs on every deploy (seen in
/// production); the hold is logged once when it starts and once when it ends instead.
/// </summary>
public sealed class ChampionWarmupReadinessLoggingTests
{
    private const string HealthCheckServiceCategory =
        "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";

    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AHeldReadinessIsLoggedOnceNotOnEveryProbe()
    {
        using var host = new Host();

        await host.ProbeAsync(times: 5);

        host.Framework.Entries.Should().BeEmpty();
        host.Check.Entries.Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Warning);
        host.Check.Messages.Single().Should().Contain("held");
    }

    [Fact]
    public async Task OpeningOnThePublishedPassIsInformationLoggedOnce()
    {
        using var host = new Host();
        await host.ProbeAsync(times: 2);

        host.Version.Publish("v1");
        await host.ProbeAsync(times: 3);

        host.Check.Entries.Select(entry => entry.Level).Should().Equal(LogLevel.Warning, LogLevel.Information);
        host.Check.Messages.Last().Should().Contain("published");
    }

    [Fact]
    public async Task OpeningColdPastTheBudgetIsAWarning()
    {
        using var host = new Host();
        await host.ProbeAsync(times: 1);

        host.Clock.Advance(TimeSpan.FromSeconds(new ChampionWarmupOptions().ReadyBudgetSeconds));
        var report = await host.ProbeAsync(times: 2);

        report.Status.Should().Be(HealthStatus.Healthy);
        host.Check.Entries.Select(entry => entry.Level).Should().Equal(LogLevel.Warning, LogLevel.Warning);
        host.Check.Messages.Last().Should().Contain("cold");
    }

    [Fact]
    public async Task AnUnhealthyCheckStillReachesTheLogs()
    {
        using var host = new Host(services => services.AddHealthChecks()
            .AddCheck("always-down", () => HealthCheckResult.Unhealthy("down"), tags: ["ready"]));

        await host.ProbeAsync(times: 1);

        host.Framework.Entries.Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Error);
    }

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _services;

        public Host(Action<IServiceCollection>? configure = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ChampionWarmup:Enabled"] = "true" })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddLogging(logging => logging
                .SetMinimumLevel(LogLevel.Trace)
                .AddProvider(Framework)
                .AddProvider(Check));
            services.AddChampionWarmup(configuration);
            configure?.Invoke(services);
            _services = services.BuildServiceProvider();
            Version = _services.GetRequiredService<ChampionReadVersion>();
        }

        public FakeTimeProvider Clock { get; } = new(Noon);

        public CapturingLoggerProvider Framework { get; } = new(HealthCheckServiceCategory, LogLevel.Information);

        public CapturingLoggerProvider Check { get; } =
            new(typeof(ChampionWarmupHealthCheck).FullName!, LogLevel.Information);

        public ChampionReadVersion Version { get; }

        public async Task<HealthReport> ProbeAsync(int times)
        {
            var health = _services.GetRequiredService<HealthCheckService>();
            HealthReport report = null!;
            for (var i = 0; i < times; i++)
            {
                report = await health.CheckHealthAsync(registration => registration.Tags.Contains("ready"));
            }

            return report;
        }

        public void Dispose() => _services.Dispose();
    }
}
