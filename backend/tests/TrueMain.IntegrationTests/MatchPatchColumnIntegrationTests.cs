using AwesomeAssertions;
using Core.Lol.Patches;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #1368: <c>matches."Patch"</c> is a stored generated column whose SQL expression is
/// meant to be the transcription of
/// <c>PatchVersion.TryParse(gameVersion, out var v) ? v.ToMajorMinor() : null</c>.
/// Nothing in the type system ties the two together, and the failure mode is silent —
/// a column that quietly disagrees with the C# rule simply drops rows out of every
/// champion read. So the two implementations are run against each other here, on the
/// real Postgres, over the awkward inputs (empty segments, whitespace, leading zeros,
/// trailing junk) rather than only the Riot-shaped ones.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchPatchColumnIntegrationTests(PostgresFixture fixture)
{
    private static readonly string[] GameVersions =
    [
        "16.4",
        "16.4.521",
        "16.4.521.123",
        "  16.4.521  ",
        "16..4",
        ".16.4",
        " 16 . 4 ",
        "16.04.5",
        "16.4x",
        "16 5.4",
        "16",
        "16.x",
        "abc.def",
        "",
        "   ",
    ];

    [Fact]
    public async Task The_generated_patch_column_agrees_with_PatchVersion_on_every_shape()
    {
        await fixture.ResetDatabaseAsync();

        await using (var seed = fixture.CreateDbContext())
        {
            for (var i = 0; i < GameVersions.Length; i++)
            {
                seed.Matches.Add(new MatchBuilder()
                    .WithId($"m-patch-col-{i}")
                    .WithGameVersion(GameVersions[i])
                    .Build());
            }

            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var stored = await db.Matches
            .AsNoTracking()
            .Where(match => match.Id.StartsWith("m-patch-col-"))
            .Select(match => new { match.Id, match.GameVersion, match.Patch })
            .ToListAsync();

        stored.Should().HaveCount(GameVersions.Length);

        foreach (var row in stored)
        {
            var expected = PatchVersion.TryParse(row.GameVersion, out var parsed)
                ? parsed.ToMajorMinor()
                : null;

            row.Patch.Should().Be(
                expected,
                "the generated column must answer '{0}' exactly as PatchVersion does",
                row.GameVersion);
        }
    }

    [Fact]
    public async Task The_generated_patch_column_follows_an_updated_game_version()
    {
        // STORED means "recomputed on write", not "computed once": a match whose
        // version is corrected must not keep the old patch, or it would sit in the
        // wrong slice of every champion read for as long as it is retained.
        await fixture.ResetDatabaseAsync();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Matches.Add(new MatchBuilder()
                .WithId("m-patch-col-update")
                .WithGameVersion("16.4.521.123")
                .Build());
            await seed.SaveChangesAsync();
        }

        await using (var update = fixture.CreateDbContext())
        {
            var match = await update.Matches.SingleAsync(m => m.Id == "m-patch-col-update");
            match.GameVersion = "16.5.700.9999";
            await update.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var patch = await db.Matches
            .AsNoTracking()
            .Where(match => match.Id == "m-patch-col-update")
            .Select(match => match.Patch)
            .SingleAsync();

        patch.Should().Be("16.5");
    }
}
