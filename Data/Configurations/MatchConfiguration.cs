using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> entity)
    {
        entity.ToTable("matches");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.QueueId)
            .IsRequired();

        entity.Property(e => e.MapId)
            .IsRequired();

        entity.Property(e => e.GameMode)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.GameType)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.GameStartTimeUtc)
            .IsRequired();

        entity.Property(e => e.GameDurationSeconds)
            .IsRequired();

        entity.Property(e => e.GameVersion)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.TimelineIngested)
            .IsRequired()
            .HasDefaultValue(false);

        entity.HasIndex(e => e.PlatformId);

        entity.HasIndex(e => e.TimelineIngested)
            .HasDatabaseName("IX_matches_timeline_ingested");

        entity.HasMany(e => e.Participants)
            .WithOne(e => e.Match)
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
