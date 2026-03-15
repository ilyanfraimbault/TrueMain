using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ProcessRunConfiguration : IEntityTypeConfiguration<ProcessRun>
{
    public void Configure(EntityTypeBuilder<ProcessRun> entity)
    {
        entity.ToTable("process_runs");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.ProcessName)
            .IsRequired()
            .HasMaxLength(64);

        entity.Property(e => e.StartedAtUtc)
            .IsRequired();

        entity.Property(e => e.FinishedAtUtc)
            .IsRequired();

        entity.Property(e => e.DurationMs)
            .IsRequired();

        entity.Property(e => e.Status)
            .IsRequired();

        entity.Property(e => e.Error)
            .HasMaxLength(2048);

        entity.Property(e => e.Host)
            .HasMaxLength(128);

        entity.Property(e => e.Summary)
            .HasColumnType("jsonb");

        entity.HasIndex(e => new { e.ProcessName, e.StartedAtUtc });
    }
}
