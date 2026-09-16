using System.Net;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Riot;
using Microsoft.Extensions.DependencyInjection;

namespace TrueMain.UnitTests;

public sealed class CompressedResponsesExtensionsTests
{
    [Fact]
    public void AcceptCompressedResponses_EnablesEveryDecompressionMethod()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("compressed").AcceptCompressedResponses();

        PrimaryDecompression(services, "compressed").Should().Be(DecompressionMethods.All);
    }

    [Fact]
    public void AcceptCompressedResponses_SurvivesTheRiotHandlerChain()
    {
        // The Riot clients stack delegating handlers and the resilience pipeline on top of the
        // primary handler; the setting has to reach the handler that actually does the I/O.
        var services = new ServiceCollection();
        services.Configure<RiotOptions>(riot => riot.ApiKey = "test-key");
        services.AddHttpClient("riot")
            .AcceptCompressedResponses()
            .AddRiotResilienceHandler();

        PrimaryDecompression(services, "riot").Should().Be(DecompressionMethods.All);
    }

    [Fact]
    public void WithoutTheExtension_TheDefaultHandlerDoesNotDecompress()
    {
        // Pins the reason the extension exists: left alone, .NET sends no Accept-Encoding.
        var services = new ServiceCollection();
        services.AddHttpClient("plain");

        PrimaryDecompression(services, "plain").Should().Be(DecompressionMethods.None);
    }

    private static DecompressionMethods PrimaryDecompression(ServiceCollection services, string clientName)
    {
        using var provider = services.BuildServiceProvider();
        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        return handler switch
        {
            HttpClientHandler clientHandler => clientHandler.AutomaticDecompression,
            SocketsHttpHandler socketsHandler => socketsHandler.AutomaticDecompression,
            _ => throw new InvalidOperationException($"Unexpected primary handler {handler.GetType().Name}."),
        };
    }
}
