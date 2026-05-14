using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TrueMain.TestKit;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that wires the host up
/// against a <see cref="PostgresFixture"/>: sets the <c>Testing</c>
/// environment, injects <c>ConnectionStrings:TrueMain</c>, a test
/// <c>Ops:ApiKey</c> that satisfies the <c>[MinLength(32)]</c> validation,
/// plus any additional overrides the test wants to add.
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var baseline = new List<KeyValuePair<string, string?>>
            {
                new("ConnectionStrings:TrueMain", fixture.ConnectionString),
                new("Ops:ApiKey", DefaultOpsApiKey)
            };

            if (extraConfiguration is { Count: > 0 })
            {
                baseline.AddRange(extraConfiguration);
            }

            configurationBuilder.AddInMemoryCollection(baseline);
        });
    }
}
