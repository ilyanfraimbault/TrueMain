using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the bulk account-freshness lookup (<c>POST /ops/accounts/freshness</c>, #1154).
///
/// <para>
/// The endpoint exists so a batch caller stops looping the per-Riot-ID explorer, so the facts
/// that matter are the ones a batch caller decides on: is it known, is it still usable, and
/// when did we last ingest it — including the case that motivated the whole thing, an account
/// that is tracked but whose claim has never come up.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AccountFreshnessApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public AccountFreshnessApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    [Fact]
    public async Task PostFreshness_ReportsKnownUnknownInvalidAndNeverIngested_InOneCall()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(Account("puuid-fresh", "Ingested", "KR1", "KR", now, now.AddDays(-2)));
            db.RiotAccounts.Add(Account("puuid-stale", "Forgotten", "KR1", "KR", now, now.AddDays(-90)));
            // Tracked, but its claim has never come up — the starved case the OTP seeder exists
            // to find, and the one a "do we know it?" check on its own would answer wrongly.
            db.RiotAccounts.Add(Account("puuid-never", "Waiting", "KR1", "KR", now, null));
            var invalid = Account("puuid-invalid", "Deleted", "KR1", "KR", now, now.AddDays(-30));
            invalid.Status = RiotAccountStatus.Invalid;
            db.RiotAccounts.Add(invalid);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var response = await client.PostAsJsonAsync("/ops/accounts/freshness", new
        {
            accounts = new[]
            {
                new { gameName = "Ingested", tagLine = "KR1", platformId = "KR" },
                new { gameName = "Forgotten", tagLine = "KR1", platformId = "KR" },
                new { gameName = "Waiting", tagLine = "KR1", platformId = "KR" },
                new { gameName = "Deleted", tagLine = "KR1", platformId = "KR" },
                new { gameName = "NeverHeardOf", tagLine = "KR1", platformId = "KR" }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FreshnessResponse>();
        body.Should().NotBeNull();
        body!.Accounts.Should().HaveCount(5, "the response mirrors the request one-for-one");

        var byName = body.Accounts.ToDictionary(entry => entry.GameName);

        byName["Ingested"].Known.Should().BeTrue();
        byName["Ingested"].LastMatchIngestAtUtc.Should().NotBeNull();

        byName["Forgotten"].Known.Should().BeTrue();
        byName["Forgotten"].LastMatchIngestAtUtc.Should().BeBefore(now.AddDays(-60));

        byName["Waiting"].Known.Should().BeTrue();
        byName["Waiting"].LastMatchIngestAtUtc.Should().BeNull();

        byName["Deleted"].Known.Should().BeTrue();
        byName["Deleted"].Status.Should().Be(nameof(RiotAccountStatus.Invalid));

        byName["NeverHeardOf"].Known.Should().BeFalse();
        byName["NeverHeardOf"].Status.Should().BeNull();
    }

    [Fact]
    public async Task PostFreshness_MatchesCaseInsensitively_AndScopesToThePlatform()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(Account("puuid-kr", "Aileri", "KR1", "KR", now, now.AddDays(-90)));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var response = await client.PostAsJsonAsync("/ops/accounts/freshness", new
        {
            accounts = new[]
            {
                // Our stored spelling drifts from the live one until AccountRefresh catches up,
                // so a case-sensitive match would report a tracked account as unknown and the
                // caller would pay a Riot call to rediscover it.
                new { gameName = "aILERI", tagLine = "kr1", platformId = "KR" },
                // A Riot ID is only unique within a platform: the same name elsewhere is a
                // different player, and answering "known" would suppress a legitimate seed.
                new { gameName = "Aileri", tagLine = "KR1", platformId = "EUW1" }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FreshnessResponse>();
        body!.Accounts[0].Known.Should().BeTrue();
        body.Accounts[1].Known.Should().BeFalse();
    }

    [Fact]
    public async Task PostFreshness_RejectsAnOversizedBatchAndAnUnknownPlatform()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var oversized = await client.PostAsJsonAsync("/ops/accounts/freshness", new
        {
            accounts = Enumerable.Range(0, 1001)
                .Select(i => new { gameName = $"p{i}", tagLine = "KR1", platformId = "KR" })
                .ToArray()
        });
        oversized.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badPlatform = await client.PostAsJsonAsync("/ops/accounts/freshness", new
        {
            accounts = new[] { new { gameName = "Aileri", tagLine = "KR1", platformId = "MARS1" } }
        });
        // Silently answering "never discovered" for a typo would be a lie the caller acts on.
        badPlatform.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostFreshness_ResolvesADuplicateRiotIdByLastActivity_NotByHavingBeenIngested()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
        var now = DateTime.UtcNow;

        // A Riot ID is mutable and recyclable, so a renamed account and whoever took its old
        // name coexist until AccountRefresh resolves them. Ranking the never-ingested row below
        // any ever-ingested one would let the stale row mask the live one — and "tracked but its
        // claim has never come up" is the case this endpoint exists to surface.
        await using (var db = _fixture.CreateDbContext())
        {
            var abandoned = Account("puuid-abandoned", "Twice", "KR1", "KR", now, now.AddDays(-200));
            abandoned.UpdatedAtUtc = now.AddDays(-200);

            var current = Account("puuid-current", "Twice", "KR1", "KR", now, null);
            current.UpdatedAtUtc = now.AddHours(-1);

            db.RiotAccounts.AddRange(abandoned, current);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var response = await client.PostAsJsonAsync("/ops/accounts/freshness", new
        {
            accounts = new[] { new { gameName = "Twice", tagLine = "KR1", platformId = "KR" } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await response.Content.ReadFromJsonAsync<FreshnessResponse>())!.Accounts.Single();
        row.Known.Should().BeTrue();
        row.LastMatchIngestAtUtc.Should().BeNull("the recently-refreshed row wins on last activity");
    }

    [Fact]
    public async Task PostFreshness_ReturnsEmpty_ForAnEmptyBatch()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateAuthedClient(factory);

        var response = await client.PostAsJsonAsync("/ops/accounts/freshness", new { accounts = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FreshnessResponse>())!.Accounts.Should().BeEmpty();
    }

    private static RiotAccount Account(
        string puuid, string gameName, string tagLine, string platformId,
        DateTime now, DateTime? lastIngest)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = now.AddDays(-30),
            UpdatedAtUtc = now,
            LastMatchIngestAtUtc = lastIngest
        };

    private static HttpClient CreateAuthedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);

    private sealed record FreshnessResponse(IReadOnlyList<FreshnessRow> Accounts);

    private sealed record FreshnessRow(
        string GameName,
        string TagLine,
        string PlatformId,
        bool Known,
        string? Status,
        DateTime? LastMatchIngestAtUtc);
}
