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

        entity.Property(e => e.LastRankSyncAtUtc);

        entity.Property(e => e.Score);

        entity.Property(e => e.LastMainCalcAtUtc);

        entity.Property(e => e.LastMatchIngestAtUtc);

        entity.Property(e => e.MatchIngestStatus)
            .IsRequired()
            .HasDefaultValue(MatchIngestStatus.Idle);

        entity.Property(e => e.MatchIngestClaimedAtUtc);

        entity.Property(e => e.Status)
            .IsRequired()
            .HasDefaultValue(RiotAccountStatus.Active);

        entity.HasIndex(e => e.Puuid)
            .IsUnique();

        entity.HasIndex(e => e.PersonaId);

        entity.HasIndex(e => new { e.GameName, e.TagLine, e.PlatformId })
            .IsUnique();

        // Partial index: the match-ingest claim/lease scan only looks at
        // non-Idle rows, but ~99% of accounts sit at Idle. Filtering Idle out
        // keeps the index small and hot for the claim query. The filter is raw
        // SQL, so MatchIngestStatus.Idle is spelled as its backing value (0).
        entity.HasIndex(e => new { e.MatchIngestStatus, e.MatchIngestClaimedAtUtc, e.LastMatchIngestAtUtc })
            .HasDatabaseName("IX_riot_accounts_ingest_claim_lease")
            .HasFilter("\"MatchIngestStatus\" <> 0");

        // Serves the leaderboard's ORDER BY Score DESC NULLS LAST pagination.
        entity.HasIndex(e => e.Score)
            .HasDatabaseName("IX_riot_accounts_score");

        entity.HasOne(e => e.Persona)
            .WithMany(p => p.RiotAccounts)
            .HasForeignKey(e => e.PersonaId);
    }
}
