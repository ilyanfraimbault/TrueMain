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

        // Defaults to true so every pre-#900 row — and any row written by a
        // producer that predates the activity check — is active until
        // MainActivityProcess proves otherwise.
        entity.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        entity.Property(e => e.IsOtp)
            .IsRequired();

        entity.Property(e => e.IsExtendedSample)
            .IsRequired()
            .HasDefaultValue(false);

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

        // (PlatformId, Puuid) single index dropped (#236): it is a leading prefix of
        // the (PlatformId, Puuid, ChampionId) unique above, which Postgres already
        // uses for (PlatformId) and (PlatformId, Puuid) lookups.

        // Covering index for MainChampionStatRepository.GetMainAccountsAsync,
        // which filters on (IsMain, IsActive, PlatformId) and projects only Puuid.
        // Including Puuid lets Postgres serve the main-account roster as an
        // index-only scan instead of probing the heap per row.
        entity.HasIndex(e => new { e.PlatformId, e.IsMain, e.IsActive })
            .IncludeProperties(e => e.Puuid);

        // Partial index for MainChampionStatRepository.GetMainCountsByChampionAsync
        // (WHERE IsMain AND IsActive GROUP BY ChampionId), the coverage signal recomputed
        // every scoring/main-analysis cycle. The (PlatformId, ...) index above cannot serve
        // a predicate without PlatformId because it leads, so this gives an index-only scan
        // over just the active main rows. Inactive mains (#900) are outside the filter, which
        // is what keeps them from counting towards Coverage:TargetMainsPerChampion.
        entity.HasIndex(e => e.ChampionId)
            .HasFilter("\"IsMain\" AND \"IsActive\"")
            .HasDatabaseName("IX_main_champion_stats_is_main_champion");

    }
}
