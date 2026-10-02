using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class TimelineLeadSigmaMomentConfiguration : IEntityTypeConfiguration<TimelineLeadSigmaMoment>
{
    public void Configure(EntityTypeBuilder<TimelineLeadSigmaMoment> entity)
    {
        entity.ToTable("timeline_lead_sigma_moments");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.QueueId).IsRequired();
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.IntervalMinute).IsRequired();
        entity.Property(e => e.N).IsRequired();
        entity.Property(e => e.SumGold).IsRequired();
        entity.Property(e => e.SumSqGold).IsRequired();
        entity.Property(e => e.SumDmg).IsRequired();
        entity.Property(e => e.SumSqDmg).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain; also the ON CONFLICT target of the
        // incremental upsert and the read seek (fold minutes within a queue+patch).
        entity.HasIndex(e => new { e.QueueId, e.Patch, e.IntervalMinute })
            .IsUnique();
    }
}
