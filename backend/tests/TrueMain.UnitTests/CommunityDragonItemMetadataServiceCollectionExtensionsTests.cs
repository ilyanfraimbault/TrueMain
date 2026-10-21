using System.Net;
using System.Text;
using AwesomeAssertions;
using Data.BuildFacts;
using Ingestor.BuildFacts;
using Microsoft.Extensions.DependencyInjection;

namespace TrueMain.UnitTests;

/// <summary>
/// The ingestor's item-metadata registration (#2212): a provider per resolution, reading
/// CommunityDragon through the named client the resilience pipeline is chained onto.
/// </summary>
public sealed class CommunityDragonItemMetadataServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddCommunityDragonItemMetadata_ResolvesAProviderPerResolution_OnTheConfiguredClient()
    {
        using var communityDragon = new CountingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services
            .AddCommunityDragonItemMetadata((_, client) => client.BaseAddress = new Uri("https://configured.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => communityDragon);
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IItemMetadataProvider>();
        var second = provider.GetRequiredService<IItemMetadataProvider>();
        second.Should().NotBeSameAs(first, "a process run holds its own provider, as the typed client did");

        var items = await first.GetItemsAsync("16.19.1", CancellationToken.None);
        items.Should().ContainKey(3143);
        provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(CommunityDragonItemMetadataProvider))
            .BaseAddress.Should().Be(new Uri("https://configured.test/"));
        communityDragon.Requests.Should().Be(1);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{ "id": 3143, "inStore": true, "priceTotal": 2700 }]""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
