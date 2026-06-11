using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class SeedRequestConfiguration : IEntityTypeConfiguration<SeedRequest>
{
    public void Configure(EntityTypeBuilder<SeedRequest> entity)
    {
        entity.ToTable("seed_requests");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        // Riot game names are at most 16 chars; 32 matches RiotAccount.GameName
        // and leaves headroom.
        entity.Property(e => e.GameName)
            .IsRequired()
            .HasMaxLength(32);

        // Tag lines are short (<=5 chars today); 8 matches RiotAccount.TagLine.
        entity.Property(e => e.TagLine)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        // Stored as the enum name (text), not the numeric value: keeps the
        // column self-describing in ad-hoc SQL and lets the /ops history filter
        // match on the status name directly. Longest name is "Resolving" (9);
        // 16 leaves headroom.
        entity.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.Error)
            .HasMaxLength(2048);

        entity.Property(e => e.RequestedAtUtc)
            .IsRequired();

        entity.Property(e => e.ProcessedAtUtc);

        entity.Property(e => e.ResolvedPuuid)
            .HasMaxLength(128);

        entity.Property(e => e.ResolvedRiotAccountId);

        // The Ingestor's claim scan filters by Status; the /ops history orders
        // newest-first by RequestedAtUtc.
        entity.HasIndex(e => e.Status);
        entity.HasIndex(e => e.RequestedAtUtc);
    }
}
