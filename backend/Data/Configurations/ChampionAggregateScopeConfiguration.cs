using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregateScopeConfiguration : IEntityTypeConfiguration<ChampionAggregateScope>
{
    public void Configure(EntityTypeBuilder<ChampionAggregateScope> entity)
    {
        entity.ToTable("champion_aggregate_scopes");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.RiotAccountId).IsRequired();
        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.GameVersion).IsRequired().HasMaxLength(32);
        entity.Property(e => e.PlatformId).IsRequired().HasMaxLength(8);
        entity.Property(e => e.QueueId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.LastGameStartTimeUtc).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        entity.HasIndex(e => new
        {
            e.RiotAccountId,
            e.ChampionId,
            e.GameVersion,
            e.PlatformId,
            e.QueueId,
            e.Position
        }).IsUnique();

        entity.HasIndex(e => new { e.RiotAccountId, e.ChampionId, e.GameVersion, e.PlatformId, e.Position });
        entity.HasIndex(e => new { e.ChampionId, e.GameVersion, e.PlatformId, e.QueueId });

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId);
    }
}
