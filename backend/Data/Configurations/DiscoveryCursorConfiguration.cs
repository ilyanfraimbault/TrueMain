using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class DiscoveryCursorConfiguration : IEntityTypeConfiguration<DiscoveryCursor>
{
    public void Configure(EntityTypeBuilder<DiscoveryCursor> entity)
    {
        entity.ToTable("discovery_cursors");

        entity.HasKey(e => e.PlatformId);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.Offset)
            .IsRequired();

        entity.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");
    }
}
