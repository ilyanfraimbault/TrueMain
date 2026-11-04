using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Personas;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Personas;
using TrueMain.Services.Personas.Pictures;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Known players' pictures (#2446): the worker writes what the channels gave, the page and the search
/// read it, and a change to the person's channels, a merge or a removal takes effect on it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PersonaPicturesIntegrationTests : IAsyncLifetime
{
    private const string YouTubePicture = "https://yt3.ggpht.com/youtube";
    private const string TwitchPicture = "https://static-cdn.jtvnw.net/twitch";
    private const string YouTubeUrl = "https://www.youtube.com/@caps";
    private const string TwitchUrl = "https://www.twitch.tv/caps";

    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;
    private readonly FakeReaders _readers = new();

    public PersonaPicturesIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task APass_WritesTheFirstChannelsPicture_ThePageAndTheSearchReadIt_AndItIsNotDueAgain()
    {
        await SeedAsync("caps", tracked: true, (PersonaLinkKind.Twitch, TwitchUrl), (PersonaLinkKind.YouTube, YouTubeUrl));
        _readers.Answer(YouTubeUrl, YouTubePicture);
        _readers.Answer(TwitchUrl, TwitchPicture);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = factory.CreateClient();
        (await PlayerAsync(client, "caps")).PictureUrl.Should().BeNull();

        using var worker = Worker(factory);
        (await worker.RunPassAsync(CancellationToken.None)).Should().Be(1);

        (await PlayerAsync(client, "caps")).PictureUrl.Should().Be(YouTubePicture);
        var search = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=caps");
        search!.Players.Single().PictureUrl.Should().Be(YouTubePicture);

        (await worker.RunPassAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task APersonWithoutATrackedAccount_IsNotRead()
    {
        await SeedAsync("caps", tracked: false, (PersonaLinkKind.YouTube, YouTubeUrl));
        _readers.Answer(YouTubeUrl, YouTubePicture);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);

        using var worker = Worker(factory);
        (await worker.RunPassAsync(CancellationToken.None)).Should().Be(0);
        _readers.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreadableChannel_KeepsThePicture_AndRetriesSooner()
    {
        await SeedAsync("caps", tracked: true, (PersonaLinkKind.YouTube, YouTubeUrl));
        await using (var db = _fixture.CreateDbContext())
        {
            await db.Personas.ExecuteUpdateAsync(set => set.SetProperty(p => p.PictureUrl, YouTubePicture));
        }

        _readers.Unreadable(YouTubeUrl);
        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);

        using var worker = Worker(factory);
        await worker.RunPassAsync(CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var persona = await read.Personas.AsNoTracking().SingleAsync();
        persona.PictureUrl.Should().Be(YouTubePicture);
        persona.PictureRefreshAtUtc.Should().BeCloseTo(DateTime.UtcNow + PersonaPictureWorker.RetryInterval, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AChannelThatLostItsPicture_ClearsIt_AndWaitsForTheFullRefresh()
    {
        await SeedAsync("caps", tracked: true, (PersonaLinkKind.YouTube, YouTubeUrl));
        await using (var db = _fixture.CreateDbContext())
        {
            await db.Personas.ExecuteUpdateAsync(set => set.SetProperty(p => p.PictureUrl, YouTubePicture));
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var worker = Worker(factory);
        await worker.RunPassAsync(CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var persona = await read.Personas.AsNoTracking().SingleAsync();
        persona.PictureUrl.Should().BeNull();
        persona.PictureRefreshAtUtc.Should().BeCloseTo(DateTime.UtcNow + PersonaPictureWorker.RefreshInterval, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ANewChannelFromASource_MakesThePersonDueAgain()
    {
        await SeedAsync("caps", tracked: true, (PersonaLinkKind.Twitch, TwitchUrl));
        _readers.Answer(TwitchUrl, TwitchPicture);
        _readers.Answer(YouTubeUrl, YouTubePicture);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var worker = Worker(factory);
        await worker.RunPassAsync(CancellationToken.None);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersonaUpsertService>().UpsertAsync(
                new()
                {
                    Slug = "caps",
                    DisplayName = "Caps",
                    Source = "leaguepedia",
                    Links = [new() { Kind = "youtube", Url = YouTubeUrl }],
                },
                CancellationToken.None);
        }

        (await worker.RunPassAsync(CancellationToken.None)).Should().Be(1);

        await using var read = _fixture.CreateDbContext();
        (await read.Personas.AsNoTracking().SingleAsync()).PictureUrl.Should().Be(YouTubePicture);
    }

    [Fact]
    public async Task Removal_ClearsThePicture()
    {
        await SeedAsync("caps", tracked: true, (PersonaLinkKind.YouTube, YouTubeUrl));
        _readers.Answer(YouTubeUrl, YouTubePicture);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var worker = Worker(factory);
        await worker.RunPassAsync(CancellationToken.None);

        Guid id;
        await using (var db = _fixture.CreateDbContext())
        {
            id = await db.Personas.Select(p => p.Id).SingleAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersonaRemovalService>().RemoveAsync(id, takedown: false, CancellationToken.None);
        }

        await using var read = _fixture.CreateDbContext();
        var persona = await read.Personas.AsNoTracking().SingleAsync();
        persona.RemovedAtUtc.Should().NotBeNull();
        persona.PictureUrl.Should().BeNull();
        (await worker.RunPassAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Merge_ClearsTheMergedPicture_AndMakesTheKeptPersonDueAgain()
    {
        await SeedAsync("caps", tracked: true, (PersonaLinkKind.Twitch, TwitchUrl));
        await SeedAsync("caps-2", tracked: true, (PersonaLinkKind.YouTube, YouTubeUrl));
        _readers.Answer(TwitchUrl, TwitchPicture);
        _readers.Answer(YouTubeUrl, YouTubePicture);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var worker = Worker(factory);
        await worker.RunPassAsync(CancellationToken.None);

        Guid keptId, mergedId;
        await using (var db = _fixture.CreateDbContext())
        {
            keptId = await db.Personas.Where(p => p.Slug == "caps").Select(p => p.Id).SingleAsync();
            mergedId = await db.Personas.Where(p => p.Slug == "caps-2").Select(p => p.Id).SingleAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PersonaMerger>().MergeAsync(keptId, mergedId, null, CancellationToken.None);
        }

        (await worker.RunPassAsync(CancellationToken.None)).Should().Be(1);

        await using var read = _fixture.CreateDbContext();
        (await read.Personas.AsNoTracking().SingleAsync(p => p.Id == mergedId)).PictureUrl.Should().BeNull();
        (await read.Personas.AsNoTracking().SingleAsync(p => p.Id == keptId)).PictureUrl.Should().Be(YouTubePicture);
    }

    private PersonaPictureWorker Worker(ApiWebApplicationFactory factory) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new PersonaPictureResolver(PersonaPictureResolver.Priority.Select(kind => new FakeReader(kind, _readers))),
        MicrosoftOptions.Create(new PersonaPictureOptions()),
        TimeProvider.System,
        NullLogger<PersonaPictureWorker>.Instance,
        TimeSpan.Zero,
        TimeSpan.Zero);

    private async Task SeedAsync(string slug, bool tracked, params (PersonaLinkKind Kind, string Url)[] links)
    {
        await using var db = _fixture.CreateDbContext();
        var persona = new Persona { Slug = slug, DisplayName = slug, Source = PersonaSource.Leaguepedia };
        foreach (var (kind, url) in links)
        {
            persona.Links.Add(new PersonaLink { Kind = kind, Url = url, Source = PersonaSource.Leaguepedia });
        }

        var account = new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = $"puuid-{slug}",
            GameName = slug,
            TagLine = "EUW",
            PlatformId = "EUW1",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };
        db.RiotAccounts.Add(account);
        persona.Accounts.Add(new PersonaAccount
        {
            GameName = slug,
            TagLine = "EUW",
            PlatformId = "EUW1",
            Source = PersonaSource.Leaguepedia,
            RiotAccountId = tracked ? account.Id : null,
        });
        db.Personas.Add(persona);
        await db.SaveChangesAsync();
    }

    private static async Task<PersonaReadModel> PlayerAsync(HttpClient client, string slug) =>
        (await client.GetFromJsonAsync<PersonaReadModel>($"/players/{slug}"))!;

    private sealed class FakeReaders
    {
        private readonly Dictionary<string, PersonaPictureLookup> _answers = [];

        public List<string> Asked { get; } = [];

        public void Answer(string url, string picture) => _answers[url] = new PersonaPictureLookup(PersonaPictureOutcome.Found, picture);

        public void Unreadable(string url) => _answers[url] = PersonaPictureLookup.Unavailable;

        public PersonaPictureLookup Read(string url)
        {
            Asked.Add(url);
            return _answers.GetValueOrDefault(url, PersonaPictureLookup.None);
        }
    }

    private sealed class FakeReader(PersonaLinkKind kind, FakeReaders readers) : IPersonaPictureReader
    {
        public PersonaLinkKind Kind => kind;

        public Task<PersonaPictureLookup> ReadAsync(string channelUrl, CancellationToken ct) => Task.FromResult(readers.Read(channelUrl));
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None"),
            ]);
}
