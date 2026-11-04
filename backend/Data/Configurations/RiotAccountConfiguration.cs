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

        entity.Property(e => e.LastActivityCheckAtUtc);

        entity.Property(e => e.LastSeenInMatchAtUtc);

        entity.Property(e => e.SummonerObservedAtUtc);

        entity.Property(e => e.MatchIngestStatus)
            .IsRequired()
            .HasDefaultValue(MatchIngestStatus.Idle);

        entity.Property(e => e.MatchIngestClaimedAtUtc);

        entity.Property(e => e.Status)
            .IsRequired()
            .HasDefaultValue(RiotAccountStatus.Active);

        // No concurrency token, on purpose (#2448). The fetch lane writes these rows in raw
        // SQL at any time, so a whole-row xmin failed every tracked save that held a row
        // across a Riot call, over columns it never writes. EF writes only the columns a save
        // changed; those have one writer (Status, the Riot ID) or several writers of a fresh
        // Riot reading, where the last write is the right one. The ingest status moves only
        // through the predicate-filtered ExecuteUpdateAsync in RiotAccountRepository.
        entity.HasIndex(e => e.Puuid)
            .IsUnique();

        // Intentionally NOT unique: the Riot ID is a mutable, recyclable third-party
        // attribute, not an identity — the PUUID above is. A player who renames frees
        // their old Riot ID for someone else, so between two refresh cycles a stale row
        // and a freshly renamed one legitimately hold the same GameName/TagLine on the
        // same platform. A unique index turned that into a 23505 that failed the whole
        // AccountRefresh batch every cycle. Reads already tolerate duplicates (they
        // order by last activity and take the first match).
        entity.HasIndex(e => new { e.GameName, e.TagLine, e.PlatformId });

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
    }
}
