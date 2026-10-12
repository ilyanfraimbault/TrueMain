using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaAccountConfiguration : IEntityTypeConfiguration<PersonaAccount>
{
    public void Configure(EntityTypeBuilder<PersonaAccount> entity)
    {
        entity.ToTable("persona_accounts");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        // Same widths as riot_accounts, whose Riot ID these copy.
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

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.ResolvedAtUtc);

        entity.HasIndex(e => e.PersonaId);

        // One persona per Riot ID on a platform is enforced case-insensitively, the way Riot
        // compares Riot IDs, by the functional unique index
        // IX_persona_accounts_platform_riot_id_lower — an expression the EF model cannot declare,
        // so it lives in the AddPersonas migration only.

        // One persona per tracked account. Partial: every unresolved row holds a null.
        entity.HasIndex(e => e.RiotAccountId)
            .IsUnique()
            .HasFilter("\"RiotAccountId\" IS NOT NULL");

        entity.HasOne(e => e.Persona)
            .WithMany(p => p.Accounts)
            .HasForeignKey(e => e.PersonaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
