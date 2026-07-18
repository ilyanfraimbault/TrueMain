using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PowerspikeSigmaStatConfiguration : IEntityTypeConfiguration<PowerspikeSigmaStat>
{
    public void Configure(EntityTypeBuilder<PowerspikeSigmaStat> entity)
    {
        entity.ToTable("powerspike_sigma_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.QueueId).IsRequired();
        entity.Property(e => e.IntervalMinute).IsRequired();
        entity.Property(e => e.SumGoldDiff).IsRequired();
        entity.Property(e => e.SumGoldDiffSq).IsRequired();
        entity.Property(e => e.SumDamageDiff).IsRequired();
        entity.Property(e => e.SumDamageDiffSq).IsRequired();
        entity.Property(e => e.SampleCount).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        entity.HasIndex(e => new { e.QueueId, e.IntervalMinute })
            .IsUnique();
    }
}
