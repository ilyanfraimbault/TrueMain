using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class BanScopeTotalConfiguration : IEntityTypeConfiguration<BanScopeTotal>
{
    public void Configure(EntityTypeBuilder<BanScopeTotal> entity)
    {
        entity.ToTable("ban_scope_totals");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20).HasColumnName("elo_bracket").HasDefaultValue(string.Empty);
        entity.Property(e => e.Matches).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain + the ON CONFLICT target of the incremental
        // upsert. One row per (patch, band), so this doubles as the read seek.
        entity.HasIndex(e => new
        {
            e.Patch,
            e.EloBracket,
        }).IsUnique().HasDatabaseName("IX_ban_scope_totals_grain");
    }
}
