using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the startup CORS guard added for issue #209: outside Development an
/// empty <c>Cors:Origins</c> must fail the boot loudly (it would otherwise ship a
/// no-op CORS policy that silently rejects the frontend in production), while a
/// populated list boots and actually allows the configured origin.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CorsStartupIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public CorsStartupIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Startup_InNonDevelopment_FailsWhenCorsOriginsEmpty()
    {
        // appsettings.json ships "Cors:Origins": [], so withholding the override
        // leaves the list empty. Production is non-Development, so ValidateOnStart
        // must reject the boot rather than silently run with a no-op policy.
        using var factory = new CorsStartupFactory(_fixture, environment: "Production", origin: null);

        var startup = () => factory.CreateClient();

        startup.Should().Throw<OptionsValidationException>(
                "an empty Cors:Origins outside Development must fail the boot, not run a no-op policy")
            .WithMessage("*Cors:Origins*");
    }

    [Fact]
    public async Task Startup_InNonDevelopment_AllowsConfiguredOriginWhenPresent()
    {
        const string allowedOrigin = "https://app.truemain.test";
        await using var factory = new CorsStartupFactory(_fixture, environment: "Production", origin: allowedOrigin);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // A CORS preflight exercises the policy directly through the CORS
        // middleware, so the assertion stays on the header that matters
        // (Access-Control-Allow-Origin) without coupling to any endpoint's
        // business logic or status code.
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/champions");
        preflight.Headers.Add("Origin", allowedOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(preflight);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle()
            .Which.Should().Be(allowedOrigin,
                "the configured origin must be echoed back so the browser accepts the cross-origin response");
    }

    /// <summary>
    /// A <see cref="WebApplicationFactory{TEntryPoint}"/> that — unlike
    /// <see cref="TrueMainWebApplicationFactory{TEntryPoint}"/> — does not inject a
    /// default <c>Cors:Origins</c>, so a test can drive the host with the origin
    /// list either empty (a null origin argument) or populated, and pick the
    /// environment that decides whether the guard fails or warns.
    /// </summary>
    private sealed class CorsStartupFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly List<KeyValuePair<string, string?>> _configuration;

        public CorsStartupFactory(PostgresFixture fixture, string environment, string? origin)
        {
            _environment = environment;
            _configuration =
            [
                new KeyValuePair<string, string?>("ConnectionStrings:TrueMain", fixture.ConnectionString),
                new KeyValuePair<string, string?>(
                    "Ops:ApiKey",
                    TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey)
            ];

            if (origin is not null)
            {
                _configuration.Add(new KeyValuePair<string, string?>("Cors:Origins:0", origin));
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(_configuration));
        }
    }
}
