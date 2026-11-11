using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TrueMain.Http;

namespace TrueMain.UnitTests;

/// <summary>
/// The API's HTTPS redirect (#2504): deployed, the API listens on plain HTTP only, and an
/// unconditional <c>UseHttpsRedirection</c> warned "Failed to determine the https port for redirect"
/// on every start without ever redirecting anything.
/// </summary>
public sealed class HttpsRedirectionTests
{
    private const string HttpsPolicyCategory = "Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware";

    [Fact]
    public async Task PlainHttpHost_AnswersDirectly_AndLogsNothingFromTheRedirect()
    {
        var (response, logs) = await SendThroughPipelineAsync("--urls", "http://+:8080");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        logs.GetSnapshot().Should().NotContain(record => record.Category == HttpsPolicyCategory);
    }

    [Fact]
    public async Task HostWithAnHttpsPort_StillRedirectsHttpToHttps()
    {
        var (response, _) = await SendThroughPipelineAsync("--https_port", "7298");

        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.Should().Be(new Uri("https://localhost:7298/"));
    }

    [Theory]
    [InlineData("urls", "https://localhost:7298;http://localhost:5008", true)]
    [InlineData("urls", "HTTPS://+:443", true)]
    [InlineData("https_ports", "8443", true)]
    [InlineData("https_port", "443", true)]
    [InlineData("urls", "http://+:8080", false)]
    [InlineData("https_ports", " ", false)]
    [InlineData("http_ports", "8080", false)]
    public void HasHttpsEndpoint_ReadsTheHostEndpointSettings(string key, string value, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        HttpsRedirection.HasHttpsEndpoint(configuration).Should().Be(expected);
    }

    [Fact]
    public void HasHttpsEndpoint_IsFalseWithNoEndpointSetting()
        => HttpsRedirection.HasHttpsEndpoint(new ConfigurationBuilder().Build()).Should().BeFalse();

    private static async Task<(HttpResponseMessage Response, FakeLogCollector Logs)> SendThroughPipelineAsync(
        params string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddFakeLogging();

        await using var app = builder.Build();
        app.UseHttpsRedirectionWhenServed();
        app.MapGet("/", () => "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
        var logs = app.Services.GetFakeLogCollector();
        await app.StopAsync(TestContext.Current.CancellationToken);
        return (response, logs);
    }
}
