using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Pins the Production fail-fast in <c>Api/Program.cs</c>: when
/// <c>ConnectionStrings:TrueMain</c> is missing, the Npgsql "ready" health check
/// is never registered, which would leave <c>/readyz</c> reporting Healthy on an
/// empty predicate while Postgres is unreachable. The host must instead throw at
/// startup. The guard is scoped to Production (all deployments run as Production),
/// so this only exercises that environment. No Postgres fixture is needed — the
/// host throws during service registration, long before it touches the database —
/// so the class deliberately stays out of the serialised integration collection.
/// </summary>
public sealed class HealthCheckBootstrapIntegrationTests
{
    [Fact]
    public async Task Build_ShouldThrow_WhenConnectionStringMissingInProduction()
    {
        await using var factory = new MissingConnectionStringFactory();

        // CreateClient() builds the host, running Program's configuration up to
        // the health-check branch (same trigger pattern as CorsStartupIntegrationTests).
        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*readiness health check can be registered in Production*");
    }

    private sealed class MissingConnectionStringFactory()
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
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
