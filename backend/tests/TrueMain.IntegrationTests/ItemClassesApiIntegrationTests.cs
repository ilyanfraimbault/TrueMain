using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Statics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrueMain.ReadModels.Items;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The item-classes endpoint: the provider's classes served by item id, names in the
/// item's own order, the patch passed through as asked, and a malformed patch refused.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ItemClassesApiIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ItemClasses_ServeEachItemsClasses_MainOneFirst_ForTheAskedPatch()
    {
        var provider = new FakeItemClassProvider();
        await using var factory = new ApiWebApplicationFactory(fixture, provider);
        using var client = Client(factory);

        var response = await client.GetAsync("/items/classes?patch=16.21.1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ItemClassesResponse>())!;
        body.Patch.Should().Be("16.21");
        body.Items.Select(item => item.ItemId).Should().Equal(3031, 3190);
        body.Items[1].Classes.Should().Equal("Support", "Tank");
        provider.Asked.Should().Equal("16.21.1");
    }

    [Fact]
    public async Task ItemClasses_RefuseAMalformedPatch()
    {
        var provider = new FakeItemClassProvider();
        await using var factory = new ApiWebApplicationFactory(fixture, provider);
        using var client = Client(factory);

        var response = await client.GetAsync("/items/classes?patch=latest%2F..");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        provider.Asked.Should().BeEmpty();
    }

    private static HttpClient Client(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>API factory whose item classes never reach CommunityDragon.</summary>
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, IItemClassProvider provider)
        : TrueMainWebApplicationFactory<Program>(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IItemClassProvider>();
                services.AddSingleton(provider);
            });
        }
    }

    private sealed class FakeItemClassProvider : IItemClassProvider
    {
        public List<string?> Asked { get; } = [];

        public Task<ItemClasses> GetClassesAsync(string? gameVersion, CancellationToken ct)
        {
            Asked.Add(gameVersion);
            IReadOnlyDictionary<int, IReadOnlyList<ItemShopClass>> items = new Dictionary<int, IReadOnlyList<ItemShopClass>>
            {
                [3190] = [ItemShopClass.Support, ItemShopClass.Tank],
                [3031] = [ItemShopClass.Marksman],
            };
            return Task.FromResult(new ItemClasses("16.21", items));
        }
    }
}
