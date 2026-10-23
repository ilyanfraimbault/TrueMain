using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaAccountExclusionConfiguration : IEntityTypeConfiguration<PersonaAccountExclusion>
{
    public void Configure(EntityTypeBuilder<PersonaAccountExclusion> entity)
    {
        entity.ToTable("persona_account_exclusions");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.GameName)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.TagLine)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        // One exclusion per persona and Riot ID, case-insensitive: the functional unique index
        // IX_persona_account_exclusions_persona_riot_id_lower lives in the AddPersonaConflicts
        // migration only. It also serves the intake's lookup, PersonaId being its prefix.

        entity.HasIndex(e => new { e.PersonaId, e.RiotAccountId });

        entity.HasOne(e => e.Persona)
            .WithMany()
            .HasForeignKey(e => e.PersonaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(e => e.Conflict)
            .WithMany()
            .HasForeignKey(e => e.ConflictId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
