using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaAccountConflictConfiguration : IEntityTypeConfiguration<PersonaAccountConflict>
{
    public void Configure(EntityTypeBuilder<PersonaAccountConflict> entity)
    {
        entity.ToTable("persona_account_conflicts");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        // Same widths as persona_accounts, whose Riot ID these copy.
        entity.Property(e => e.GameName)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.TagLine)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.Resolution)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.FirstSeenAtUtc)
            .IsRequired();

        entity.Property(e => e.LastSeenAtUtc)
            .IsRequired();

        entity.Property(e => e.TimesSeen)
            .IsRequired();

        entity.Property(e => e.ResolvedAtUtc);

        // One row per claiming persona and Riot ID, case-insensitive: the functional unique index
        // IX_persona_account_conflicts_claimant_riot_id_lower lives in the AddPersonaConflicts
        // migration only, like persona_accounts' own.

        // The admin queue: open conflicts, most recently seen first.
        entity.HasIndex(e => new { e.Status, e.LastSeenAtUtc });

        entity.HasIndex(e => e.OwnerPersonaId);

        entity.HasIndex(e => e.RiotAccountId);

        entity.HasOne(e => e.Claimant)
            .WithMany()
            .HasForeignKey(e => e.ClaimantPersonaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerPersonaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
