using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PersonaConfiguration : IEntityTypeConfiguration<Persona>
{
    public const int SlugMaxLength = 64;
    public const int DisplayNameMaxLength = 64;

    /// <summary>The homonym key of <c>"DisplayName"</c> (#2271), the generation expression of <c>"NameKey"</c>.</summary>
    public const string NameKeySql = "regexp_replace(lower(\"DisplayName\"), '[[:space:]._-]+', '', 'g')";

    /// <summary>A persona neither merged nor removed: the only ones homonym groups are made of.</summary>
    public const string LiveFilterSql = "\"MergedIntoPersonaId\" IS NULL AND \"RemovedAtUtc\" IS NULL";

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

        entity.Property(e => e.NameKey)
            .IsRequired()
            .HasComputedColumnSql(NameKeySql, stored: true);

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

        // The homonym groups (#2271) are a GROUP BY of the live personas' name keys: counted from
        // this index alone for the admin's sidebar badge (#2294), with the id for the marked pairs.
        entity.HasIndex(e => e.NameKey)
            .HasFilter(LiveFilterSql)
            .IncludeProperties(e => e.Id);

        // Restrict: a persona others were merged into is never deleted from under them.
        entity.HasOne(e => e.MergedInto)
            .WithMany()
            .HasForeignKey(e => e.MergedIntoPersonaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
