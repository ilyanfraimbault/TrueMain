using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PaceSampledMatchConfiguration : IEntityTypeConfiguration<PaceSampledMatch>
{
    public void Configure(EntityTypeBuilder<PaceSampledMatch> entity)
    {
        entity.ToTable("pace_sampled_matches");

        entity.HasKey(e => e.MatchId);
        entity.Property(e => e.MatchId).HasMaxLength(64);
        entity.Property(e => e.Tier).IsRequired().HasMaxLength(20);
        entity.Property(e => e.SampledAtUtc).IsRequired();

        // The prune deletes by age.
        entity.HasIndex(e => e.SampledAtUtc);
    }
}
