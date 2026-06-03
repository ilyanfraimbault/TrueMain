using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MainChampionStatConfiguration : IEntityTypeConfiguration<MainChampionStat>
{
    public void Configure(EntityTypeBuilder<MainChampionStat> entity)
    {
        entity.ToTable("main_champion_stats");

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

        entity.Property(e => e.TotalMatches)
            .IsRequired();

        entity.Property(e => e.ChampionMatches)
            .IsRequired();

        entity.Property(e => e.PlayRate)
            .IsRequired();

        entity.Property(e => e.IsMain)
            .IsRequired();

        entity.Property(e => e.IsOtp)
            .IsRequired();

        entity.Property(e => e.PrimaryPosition)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.PositionBreakdown)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(e => e.CalculatedAtUtc)
            .IsRequired();

        entity.HasIndex(e => new { e.PlatformId, e.Puuid, e.ChampionId })
            .IsUnique();

        entity.HasIndex(e => new { e.PlatformId, e.Puuid });

        // Covering index for MainChampionStatRepository.GetMainAccountsAsync,
        // which filters on (IsMain, PlatformId) and projects only Puuid.
        // Including Puuid lets Postgres serve the main-account roster as an
        // index-only scan instead of probing the heap per row.
        entity.HasIndex(e => new { e.PlatformId, e.IsMain })
            .IncludeProperties(e => e.Puuid);

    }
}
