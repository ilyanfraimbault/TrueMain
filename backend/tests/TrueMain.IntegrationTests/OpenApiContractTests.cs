using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

namespace TrueMain.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task CommittedContract_MatchesTheApi()
    {
        await using var factory = new ContractFactory();
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
        await using var stream = new MemoryStream();
        await document.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_1, TestContext.Current.CancellationToken);
        var actual = System.Text.Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n";

        var path = ContractPath();
        if (Environment.GetEnvironmentVariable("TRUEMAIN_UPDATE_CONTRACT") == "1")
        {
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
        }

        var committed = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        committed.Should().Be(actual);
    }

    private static string ContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent!;
        }

        return Path.Combine(directory.FullName, "web", "shared", "contract", "openapi.json");
    }

    private sealed class ContractFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new("ConnectionStrings:TrueMain", "Host=contract.invalid;Database=contract"),
                new("Ops:ApiKey", TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey),
                new("Cors:Origins:0", TrueMainWebApplicationFactory<Program>.DefaultCorsOrigin),
            ]));
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
