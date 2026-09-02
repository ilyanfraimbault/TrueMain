using AwesomeAssertions;
using Data;
using Data.Configurations;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class PatchFilterTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("16.4", "16.4")]
    [InlineData("16.4.521", "16.4")]
    [InlineData("16.4.521.123", "16.4")]
    [InlineData("  16.4.521  ", "16.4")]
    [InlineData("16", null)]
    [InlineData("16.x", null)]
    [InlineData("abc.def", null)]
    // The cases the stored generated column has to agree on, spelled out here so the
    // C# side of the contract is pinned next to the SQL one (#1368): empty segments
    // are dropped, each segment is trimmed, and a leading zero is a parsed integer,
    // not a character. MatchPatchColumnParityTests re-runs the same table against
    // Postgres.
    [InlineData("16..4", "16.4")]
    [InlineData(".16.4", "16.4")]
    [InlineData(" 16 . 4 ", "16.4")]
    [InlineData("16.04.5", "16.4")]
    [InlineData("16.4x", null)]
    [InlineData("16 5.4", null)]
    public void Normalize_TrimsTrailingSegmentsToMajorMinor(string? input, string? expected)
    {
        PatchFilter.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void The_patch_filter_reads_the_generated_column_rather_than_a_like_prefix()
    {
        // The reason PatchFilter lost its Prefix()/NormalizedPrefix() helpers: the
        // narrowing is an equality against matches."Patch" now, and that only holds
        // as long as the column really is the stored generated one. A model that
        // mapped Patch as an ordinary column would still compile and still return the
        // right rows — while writing NULL into it for every ingested match.
        using var db = SqlShapeContext();
        var patch = db.Model.FindEntityType(typeof(Match))!.FindProperty(nameof(Match.Patch))!;

        patch.GetComputedColumnSql().Should().Be(MatchConfiguration.PatchComputedColumnSql);
        patch.GetIsStored().Should().BeTrue();
    }

    [Fact]
    public void The_generated_column_is_indexed_for_the_champion_reads()
    {
        using var db = SqlShapeContext();
        var indexes = db.Model.FindEntityType(typeof(Match))!.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToList();

        indexes.Should().Contain("IX_matches_patch_queue");
        indexes.Should().Contain("IX_matches_queue_patch_platform");
    }

    private static TrueMainDbContext SqlShapeContext()
    {
        // No database is opened: building the model is enough to assert its shape.
        var options = new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=truemain-sql-shape;Username=none;Password=none")
            .Options;

        return new TrueMainDbContext(options);
    }
}
