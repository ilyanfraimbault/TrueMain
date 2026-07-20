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

        // Optimistic concurrency via Postgres' system xmin column (#231). The
        // candidate Status is advanced concurrently by the discovery, scoring and
        // main-analysis passes; xmin makes a tracked SaveChanges fail loudly on a
        // lost update instead of silently overwriting. System column, so no
        // physical schema change — a shadow concurrency token only.
        entity.UseXminAsConcurrencyToken();

        entity.HasIndex(e => new { e.PlatformId, e.Puuid, e.ChampionId })
            .IsUnique();

        // (PlatformId) and (ChampionId) single-column indexes dropped (#236): the
        // composite unique above is a prefix index for any PlatformId-leading scan,
        // and no query filters MainCandidate by ChampionId alone (the only
        // ChampionId predicate is the composite-covered (PlatformId, Puuid,
        // ChampionId) lookup in MainCandidateRepository).
        entity.HasIndex(e => new { e.PlatformId, e.Status, e.Score });
    }
}
