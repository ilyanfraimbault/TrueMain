using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PaceBenchmarkStatConfiguration : IEntityTypeConfiguration<PaceBenchmarkStat>
{
    public void Configure(EntityTypeBuilder<PaceBenchmarkStat> entity)
    {
        entity.ToTable("pace_benchmark_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Tier).IsRequired().HasMaxLength(20);
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Minute).IsRequired();
        // Text, per the enum rule (decisions/backend-conventions.md): an aggregate key read
        // in ad-hoc SQL, not an internal flow flag.
        entity.Property(e => e.Metric).IsRequired().HasMaxLength(16).HasConversion<string>();
        entity.Property(e => e.Bucket).IsRequired();
        entity.Property(e => e.Count).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain + the ON CONFLICT target of the incremental upsert.
        // Position leads because the read is "every tier and minute of one position over
        // the last few patches".
        entity.HasIndex(e => new
        {
            e.Position,
            e.Patch,
            e.Tier,
            e.Minute,
            e.Metric,
            e.Bucket,
        }).IsUnique().HasDatabaseName("IX_pace_benchmark_stats_grain");
    }
}
