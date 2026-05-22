using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class RankSnapshotConfiguration : IEntityTypeConfiguration<RankSnapshot>
{
    public void Configure(EntityTypeBuilder<RankSnapshot> entity)
    {
        entity.ToTable("rank_snapshots");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.RiotAccountId)
            .IsRequired();

        entity.Property(e => e.CapturedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.Tier)
            .IsRequired()
            .HasMaxLength(16);

        entity.Property(e => e.Division)
            .IsRequired()
            .HasMaxLength(4);

        entity.Property(e => e.LeaguePoints)
            .IsRequired();

        entity.Property(e => e.Wins);

        entity.Property(e => e.Losses);

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.RiotAccountId, e.CapturedAtUtc })
            .HasDatabaseName("IX_rank_snapshots_account_captured")
            .IsDescending(false, true);
    }
}
