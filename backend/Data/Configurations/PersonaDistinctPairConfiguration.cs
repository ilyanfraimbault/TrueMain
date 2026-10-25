using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaDistinctPairConfiguration : IEntityTypeConfiguration<PersonaDistinctPair>
{
    public void Configure(EntityTypeBuilder<PersonaDistinctPair> entity)
    {
        entity.ToTable("persona_distinct_pairs", table =>
            table.HasCheckConstraint("CK_persona_distinct_pairs_ordered", "\"PersonaId\" < \"OtherPersonaId\""));

        entity.HasKey(e => new { e.PersonaId, e.OtherPersonaId });

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.HasIndex(e => e.OtherPersonaId);

        entity.HasOne(e => e.Persona)
            .WithMany()
            .HasForeignKey(e => e.PersonaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.OtherPersona)
            .WithMany()
            .HasForeignKey(e => e.OtherPersonaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
