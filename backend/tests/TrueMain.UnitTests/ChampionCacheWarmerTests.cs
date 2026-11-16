using System.Collections.Concurrent;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Directory;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Warmup;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

public sealed class ChampionCacheWarmerTests
{
    private static readonly DateTime Stamp = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task APassPublishesOnceItHasRequestedTheWholeCatalog()
    {
        using var api = new StubApi();
        await using var subject = await Running.StartAsync(api);

        await subject.WaitUntilAsync(() => subject.Version.HasPublished);

        api.Urls.Should().Contain("/champions/directory?pageSize=50&patch=16.21&eloBracket=MASTER_PLUS");
        api.Urls.Should().Contain("/truemains?page=1&pageSize=25");
        api.Urls.Should().Contain("/champions/99/synergies?position=UTILITY&eloBracket=MASTER_PLUS&patch=16.21");
    }

    [Fact]
    public async Task EveryRequestOfAPassCarriesTheKeyAndTheVersionItPublishes()
    {
        using var api = new StubApi();
        await using var subject = await Running.StartAsync(api);

        await subject.WaitUntilAsync(() => subject.Version.HasPublished);

        var header = api.PassHeaders.Should().ContainSingle().Subject;
        header.Should().StartWith(subject.Version.RequestKey + ":");
        header[(subject.Version.RequestKey.Length + 1)..].Should().Be(subject.Version.Current);
    }

    [Fact]
    public async Task ThePassGoesThroughTheServersOwnPortOverLoopback()
    {
        using var api = new StubApi();
        await using var subject = await Running.StartAsync(api, serverAddress: "http://+:8080");

        await subject.WaitUntilAsync(() => subject.Version.HasPublished);

        api.Hosts.Should().ContainSingle().Which.Should().Be("127.0.0.1:8080");
    }

    [Fact]
    public async Task APassThatMostlyFailedLeavesReadersWhereTheyAre()
    {
        using var api = new StubApi { FailEverythingButTheLines = true };
        await using var subject = await Running.StartAsync(api);

        await subject.WaitUntilAsync(() => api.Urls.Count > 1000);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        subject.Version.HasPublished.Should().BeFalse();
    }

    [Fact]
    public async Task ASiteThatNeverPublishedRetriesWithoutWaitingOutTheInterval()
    {
        using var api = new StubApi { FailEverythingButTheLines = true };
        await using var subject = await Running.StartAsync(api);
        await subject.WaitUntilAsync(() => api.Urls.Count > 1000);

        api.FailEverythingButTheLines = false;

        await subject.WaitUntilAsync(() => subject.Version.HasPublished);
    }

    [Fact]
    public async Task AWarmerThatCannotReadTheStampKeepsGoing()
    {
        using var api = new StubApi();
        var stamp = new StubStamp { Failing = true };
        await using var subject = await Running.StartAsync(api, stamp: stamp);

        await Task.Delay(TimeSpan.FromMilliseconds(50));
        subject.Version.HasPublished.Should().BeFalse();

        stamp.Failing = false;

        await subject.WaitUntilAsync(() => subject.Version.HasPublished);
    }

    [Fact]
    public async Task AServerWithNoPlainHttpAddressPublishesNothing()
    {
        using var api = new StubApi();
        await using var subject = await Running.StartAsync(api, serverAddress: "https://+:8443");

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        subject.Version.HasPublished.Should().BeFalse();
        api.Urls.Should().BeEmpty();
    }

    private sealed class Running : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly StubServer _server;
        private readonly ChampionCacheWarmer _warmer;

        private Running(StubApi api, string serverAddress, StubStamp? stamp)
        {
            Version = TestChampionReadCache.NoWarmup();
            _services = new ServiceCollection()
                .AddSingleton<IChampionAggregationStamp>(stamp ?? new StubStamp())
                .AddSingleton<IChampionSummariesQueryService>(new StubSummaries())
                .BuildServiceProvider();
            _server = new StubServer(serverAddress);
            var options = new ChampionWarmupOptions { StampPollSeconds = 1 };
            _warmer = new ChampionCacheWarmer(
                _services.GetRequiredService<IServiceScopeFactory>(),
                _server,
                new StartedLifetime(),
                new StubHttpClientFactory(api),
                Version,
                Microsoft.Extensions.Options.Options.Create(options),
                TimeProvider.System,
                NullLogger<ChampionCacheWarmer>.Instance);
        }

        public static async Task<Running> StartAsync(
            StubApi api, string serverAddress = "http://127.0.0.1:5099", StubStamp? stamp = null)
        {
            var running = new Running(api, serverAddress, stamp);
            await running._warmer.StartAsync(CancellationToken.None);
            return running;
        }

        public ChampionReadVersion Version { get; }

        public async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (!condition())
            {
                DateTime.UtcNow.Should().BeBefore(deadline, "the warmer should have got there by now");
                await Task.Delay(TimeSpan.FromMilliseconds(5));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _warmer.StopAsync(CancellationToken.None);
            _warmer.Dispose();
            _server.Dispose();
            await _services.DisposeAsync();
        }
    }

    private sealed class StubApi : HttpMessageHandler
    {
        private const string Lines = """[{"championId":99,"position":"UTILITY"},{"championId":99,"position":"MIDDLE"}]""";

        public ConcurrentBag<string> Urls { get; } = [];

        public ConcurrentDictionary<string, byte> SeenHeaders { get; } = new();

        public ConcurrentDictionary<string, byte> SeenHosts { get; } = new();

        public IEnumerable<string> PassHeaders => SeenHeaders.Keys;

        public IEnumerable<string> Hosts => SeenHosts.Keys;

        public volatile bool FailEverythingButTheLines;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Urls.Add(url);
            SeenHosts.TryAdd(request.RequestUri.Authority, 0);
            SeenHeaders.TryAdd(request.Headers.GetValues(ChampionReadVersion.HeaderName).Single(), 0);

            var isLines = url.StartsWith("/champions?", StringComparison.Ordinal) || url == "/champions";
            var status = FailEverythingButTheLines && !isLines ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(isLines ? Lines : "{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(StubApi api) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(api, disposeHandler: false);
    }

    private sealed class StubStamp : IChampionAggregationStamp
    {
        public volatile bool Failing;

        public Task<DateTime?> GetLatestAsync(CancellationToken ct)
            => Failing ? throw new InvalidOperationException("Postgres is restarting.") : Task.FromResult<DateTime?>(Stamp);
    }

    private sealed class StubSummaries : IChampionSummariesQueryService
    {
        public Task<ChampionSummariesResult> GetAllSummariesAsync(string? patch, string? eloBracket, bool truemainsOnly, CancellationToken ct)
            => Task.FromResult(new ChampionSummariesResult());

        public Task<long> GetTotalGamesAsync(CancellationToken ct) => Task.FromResult(0L);

        public Task<IReadOnlyList<string>> GetPatchesNewestFirstAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(["16.21", "16.20", "16.19"]);
    }

    private sealed class StubServer : IServer
    {
        public StubServer(string address)
        {
            var addresses = new ServerAddressesFeature();
            addresses.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(addresses);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public void Dispose()
        {
        }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ServerAddressesFeature : IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; } = [];

        public bool PreferHostingUrls { get; set; }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = new(canceled: true);

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
