using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaLinkExclusionConfiguration : IEntityTypeConfiguration<PersonaLinkExclusion>
{
    public void Configure(EntityTypeBuilder<PersonaLinkExclusion> entity)
    {
        entity.ToTable("persona_link_exclusions");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Url)
            .IsRequired()
            .HasMaxLength(PersonaLinkConfiguration.UrlMaxLength);

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        // Also the intake's lookup index: PersonaId is its prefix.
        entity.HasIndex(e => new { e.PersonaId, e.Url })
            .IsUnique();

        entity.HasOne(e => e.Persona)
            .WithMany()
            .HasForeignKey(e => e.PersonaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
