using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> entity)
    {
        entity.ToTable("log_entries");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.TimestampUtc)
            .IsRequired();

        // LogLevel names top out at "Information" (11 chars); 16 leaves headroom.
        entity.Property(e => e.Level)
            .IsRequired()
            .HasMaxLength(16);

        entity.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(256);

        entity.Property(e => e.Message)
            .IsRequired();

        // Exceptions (full ToString with stack) can be large; leave as text.
        entity.Property(e => e.Exception);

        entity.Property(e => e.ProcessName)
            .HasMaxLength(64);

        entity.Property(e => e.Host)
            .HasMaxLength(128);

        // Newest-first listing is the dominant read on /ops/logs; a descending
        // index on the timestamp lets Postgres serve the default page without a
        // sort. Level and Category back the equality filters.
        entity.HasIndex(e => e.TimestampUtc)
            .IsDescending();
        entity.HasIndex(e => e.Level);
        entity.HasIndex(e => e.Category);
    }
}
