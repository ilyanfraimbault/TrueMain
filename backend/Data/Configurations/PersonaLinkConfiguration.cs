using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaLinkConfiguration : IEntityTypeConfiguration<PersonaLink>
{
    public const int UrlMaxLength = 512;

    public void Configure(EntityTypeBuilder<PersonaLink> entity)
    {
        entity.ToTable("persona_links");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.Url)
            .IsRequired()
            .HasMaxLength(UrlMaxLength);

        entity.Property(e => e.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        // Also the lookup index for a persona's links: PersonaId is its prefix.
        entity.HasIndex(e => new { e.PersonaId, e.Url })
            .IsUnique();

        entity.HasOne(e => e.Persona)
            .WithMany(p => p.Links)
            .HasForeignKey(e => e.PersonaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
