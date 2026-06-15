using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Boots the API host with no <c>ConnectionStrings:TrueMain</c> to pin the
/// fail-fast contract: outside Development a missing connection string must
/// throw at startup rather than silently leaving the "ready" health-check tag
/// without registrations (which would make <c>/readyz</c> report Healthy while
/// Postgres is unreachable). These cases need no Postgres fixture — the host
/// never reaches the database — so the class stays out of the integration
/// collection that serialises around the shared container.
/// </summary>
public sealed class HealthCheckBootstrapIntegrationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Build_ShouldThrow_WhenConnectionStringMissingOutsideDevelopment(string environment)
    {
        using var factory = new MissingConnectionStringFactory(environment);

        // Touching Services forces the host to build, running Program's
        // configuration up to the health-check branch.
        var act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TrueMain is required outside Development*");
    }

    private sealed class MissingConnectionStringFactory(string environment)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                // Null out the connection string in the highest-precedence source
                // so any value inherited from earlier providers is overridden, and
                // satisfy OpsOptions' [MinLength(32)] so the missing-connection
                // branch is the only thing left to fail the boot.
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TrueMain"] = null,
                    ["Ops:ApiKey"] = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey
                });
            });
        }
    }
}
