using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class LadderSyncCursorConfiguration : IEntityTypeConfiguration<LadderSyncCursor>
{
    public void Configure(EntityTypeBuilder<LadderSyncCursor> entity)
    {
        entity.ToTable("ladder_sync_cursors");

        entity.HasKey(e => e.PlatformId);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.Tier)
            .IsRequired()
            .HasMaxLength(16);

        entity.Property(e => e.Division)
            .IsRequired()
            .HasMaxLength(4);

        entity.Property(e => e.Page)
            .IsRequired();

        entity.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");
    }
}
