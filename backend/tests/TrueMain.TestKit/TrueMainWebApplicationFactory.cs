using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TrueMain.TestKit;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that wires the host up
/// against a <see cref="PostgresFixture"/>: sets the <c>Testing</c>
/// environment, injects <c>ConnectionStrings:TrueMain</c>, a test
/// <c>Ops:ApiKey</c> that satisfies the <c>[MinLength(32)]</c> validation,
/// a default <c>Cors:Origins</c> entry (Testing is non-Development, so the
/// startup CORS guard fails the boot when the list is empty), plus any
/// additional overrides the test wants to add.
/// </summary>
public class TrueMainWebApplicationFactory<TEntryPoint>(
    PostgresFixture fixture,
    IReadOnlyCollection<KeyValuePair<string, string?>>? extraConfiguration = null)
    : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>
    /// A 40-char fixed value wide enough to satisfy OpsOptions'
    /// <c>[MinLength(32)]</c> DataAnnotation in tests that never call
    /// <c>/ops/*</c>.
    /// </summary>
    public const string DefaultOpsApiKey = "test-kit-ops-key-0123456789-abcdefghijklmnop";

    /// <summary>
    /// A single allowed origin so the startup CORS guard (which fails the boot
    /// outside Development when <c>Cors:Origins</c> is empty) is satisfied for
    /// the <c>Testing</c> environment tests run under.
    /// </summary>
    public const string DefaultCorsOrigin = "https://frontend.test.truemain.local";

    /// <summary>
    /// The clock the host reads instead of <see cref="System.TimeProvider.System"/>, for a test
    /// whose seed and assertion are both anchored to "now": frozen with a
    /// <see cref="FixedTimeProvider"/>, the service and the test agree on the instant even when
    /// the run straddles UTC midnight. Left unset, the host keeps the wall clock.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (TimeProvider is { } timeProvider)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            });
        }

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var baseline = new List<KeyValuePair<string, string?>>
            {
                new("ConnectionStrings:TrueMain", fixture.ConnectionString),
                new("Ops:ApiKey", DefaultOpsApiKey),
                new("Cors:Origins:0", DefaultCorsOrigin)
            };

            if (extraConfiguration is { Count: > 0 })
            {
                baseline.AddRange(extraConfiguration);
            }

            configurationBuilder.AddInMemoryCollection(baseline);
        });
    }
}
