using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchWinProbabilityConfiguration : IEntityTypeConfiguration<MatchWinProbability>
{
    public void Configure(EntityTypeBuilder<MatchWinProbability> entity)
    {
        entity.ToTable("match_win_probability");

        entity.HasKey(e => e.MatchId);

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        // Read whole by the match detail, never filtered on in SQL: jsonb, like
        // match_participants.ItemEvents.
        entity.Property(e => e.Points)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(e => e.Swings)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(e => e.Objectives)
            .HasColumnType("jsonb")
            .IsRequired();

        // Hard FK to matches, cascading like the timeline snapshots: retention deletes
        // the match and the row goes with it.
        entity.HasOne<Match>()
            .WithMany()
            .HasForeignKey(e => e.MatchId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
