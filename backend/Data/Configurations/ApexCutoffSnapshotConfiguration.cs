using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ApexCutoffSnapshotConfiguration : IEntityTypeConfiguration<ApexCutoffSnapshot>
{
    public void Configure(EntityTypeBuilder<ApexCutoffSnapshot> entity)
    {
        entity.ToTable("apex_cutoff_snapshots");

        entity.HasKey(e => new { e.PlatformId, e.Tier, e.CapturedOnUtc });

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.Tier)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<RankTierConverter>();

        entity.Property(e => e.CapturedOnUtc)
            .IsRequired();

        entity.Property(e => e.MinLeaguePoints)
            .IsRequired();

        entity.Property(e => e.EntryCount)
            .IsRequired();

        entity.Property(e => e.UpdatedAtUtc)
            .IsRequired();
    }
}
