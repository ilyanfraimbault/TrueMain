using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaConfiguration : IEntityTypeConfiguration<Persona>
{
    public const int SlugMaxLength = 64;
    public const int DisplayNameMaxLength = 64;

    public void Configure(EntityTypeBuilder<Persona> entity)
    {
        entity.ToTable("personas");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(SlugMaxLength);

        entity.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(DisplayNameMaxLength);

        entity.Property(e => e.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.HasIndex(e => e.Slug)
            .IsUnique();
    }
}
