using Data.Entities;
using AwesomeAssertions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class RiotAccountRiotIdCollisionTests
{
    private const string Platform = "KR";

    private readonly PostgresFixture _fixture;

    public RiotAccountRiotIdCollisionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RenamingAnAccountOntoAnotherRowsRiotId_DoesNotViolateAConstraint()
    {
        // Riot IDs are recyclable: when a player renames, their old Riot ID is
        // free for someone else to take. Between two refresh cycles a stale row
        // and a freshly renamed one therefore legitimately carry the same
        // GameName/TagLine on the same platform. This used to hit a unique index
        // and fail the entire AccountRefresh batch (23505), leaving every account
        // in the batch unrefreshed cycle after cycle.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.RiotAccounts.Add(NewAccount("puuid-stale", "Faker", "KR1", now));
            seed.RiotAccounts.Add(NewAccount("puuid-renamed", "Someone", "KR2", now));
            await seed.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var renamed = db.RiotAccounts.Single(a => a.Puuid == "puuid-renamed");
            renamed.GameName = "Faker";
            renamed.TagLine = "KR1";
            renamed.UpdatedAtUtc = now;

            var save = async () => await db.SaveChangesAsync();
            await save.Should().NotThrowAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        verify.RiotAccounts
            .Count(a => a.GameName == "Faker" && a.TagLine == "KR1" && a.PlatformId == Platform)
            .Should().Be(2);
    }

    private static RiotAccount NewAccount(string puuid, string gameName, string? tagLine, DateTime updatedAtUtc)
        => new()
        {
            Puuid = puuid,
            PlatformId = Platform,
            GameName = gameName,
            TagLine = tagLine,
            SummonerId = $"sum-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = updatedAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };
}
