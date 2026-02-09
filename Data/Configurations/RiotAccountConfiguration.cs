using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class RiotAccountConfiguration : IEntityTypeConfiguration<RiotAccount>
{
    public void Configure(EntityTypeBuilder<RiotAccount> entity)
    {
        entity.ToTable("riot_accounts");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Puuid)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(e => e.GameName)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.TagLine)
            .HasMaxLength(8);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.PersonaId);

        entity.Property(e => e.SummonerId)
            .HasMaxLength(128);

        entity.Property(e => e.ProfileIconId)
            .IsRequired();

        entity.Property(e => e.SummonerLevel)
            .IsRequired();

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.LastProfileSyncAtUtc);

        entity.Property(e => e.LastMainCalcAtUtc);

        entity.Property(e => e.LastMatchIngestAtUtc);

        entity.Property(e => e.MatchIngestStatus)
            .IsRequired()
            .HasDefaultValue(MatchIngestStatus.Idle);

        entity.Property(e => e.MatchIngestClaimedAtUtc);

        entity.HasIndex(e => e.Puuid)
            .IsUnique();

        entity.HasIndex(e => e.PersonaId);

        entity.HasIndex(e => new { e.GameName, e.TagLine, e.PlatformId })
            .IsUnique();

        entity.HasIndex(e => new { e.MatchIngestStatus, e.MatchIngestClaimedAtUtc, e.LastMatchIngestAtUtc })
            .HasDatabaseName("IX_riot_accounts_ingest_claim_lease");

        entity.HasOne(e => e.Persona)
            .WithMany(p => p.RiotAccounts)
            .HasForeignKey(e => e.PersonaId);
    }
}
