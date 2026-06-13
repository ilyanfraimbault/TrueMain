using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MainCandidateConfiguration : IEntityTypeConfiguration<MainCandidate>
{
    public void Configure(EntityTypeBuilder<MainCandidate> entity)
    {
        entity.ToTable("main_candidates");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.Puuid)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(e => e.ChampionId)
            .IsRequired();

        entity.Property(e => e.ChampionRankInMasteryTop)
            .IsRequired();

        entity.Property(e => e.ChampionPoints)
            .IsRequired();

        entity.Property(e => e.Source)
            .IsRequired()
            .HasDefaultValue(MainCandidateSource.Ladder);

        entity.Property(e => e.ObservedGames)
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(e => e.ObservedWins)
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(e => e.LastPlayTimeUtc)
            .IsRequired();

        entity.Property(e => e.DiscoveredAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.Score)
            .IsRequired();

        entity.Property(e => e.Status)
            .IsRequired();

        entity.Property(e => e.ScoredAtUtc);

        entity.Property(e => e.ValidatedAtUtc);

        entity.HasIndex(e => new { e.PlatformId, e.Puuid, e.ChampionId })
            .IsUnique();

        entity.HasIndex(e => e.PlatformId);

        entity.HasIndex(e => e.ChampionId);

        entity.HasIndex(e => new { e.PlatformId, e.Status, e.Score });
    }
}
