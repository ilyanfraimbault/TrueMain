using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionPatternAggregateConfiguration : IEntityTypeConfiguration<ChampionPatternAggregate>
{
    public void Configure(EntityTypeBuilder<ChampionPatternAggregate> entity)
    {
        entity.ToTable("champion_pattern_aggregates");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.RiotAccountId).IsRequired();
        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.GameVersion).IsRequired().HasMaxLength(32);
        entity.Property(e => e.PlatformId).IsRequired().HasMaxLength(8);
        entity.Property(e => e.QueueId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.PrimaryStyleId).IsRequired();
        entity.Property(e => e.SubStyleId).IsRequired();
        entity.Property(e => e.PerksOffense).IsRequired();
        entity.Property(e => e.PerksFlex).IsRequired();
        entity.Property(e => e.PerksDefense).IsRequired();
        entity.Property(e => e.SummonerSpell1Id).IsRequired();
        entity.Property(e => e.SummonerSpell2Id).IsRequired();
        entity.Property(e => e.SkillOrderKey).IsRequired().HasMaxLength(32);
        entity.Property(e => e.StarterItems).IsRequired().HasColumnType("jsonb");
        entity.Property(e => e.StarterItemsKey).IsRequired().HasMaxLength(64);
        entity.Property(e => e.BootsItemId).IsRequired();
        entity.Property(e => e.BuildItem0).IsRequired();
        entity.Property(e => e.BuildItem1).IsRequired();
        entity.Property(e => e.BuildItem2).IsRequired();
        entity.Property(e => e.BuildItem3).IsRequired();
        entity.Property(e => e.BuildItem4).IsRequired();
        entity.Property(e => e.BuildItem5).IsRequired();
        entity.Property(e => e.BuildItem6).IsRequired();
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
            e.Position,
            e.PrimaryStyleId,
            e.SubStyleId,
            e.PerksOffense,
            e.PerksFlex,
            e.PerksDefense,
            e.SummonerSpell1Id,
            e.SummonerSpell2Id,
            e.SkillOrderKey,
            e.StarterItemsKey,
            e.BootsItemId,
            e.BuildItem0,
            e.BuildItem1,
            e.BuildItem2,
            e.BuildItem3,
            e.BuildItem4,
            e.BuildItem5,
            e.BuildItem6
        }).IsUnique();

        entity.HasIndex(e => new { e.RiotAccountId, e.ChampionId, e.GameVersion, e.PlatformId, e.Position });
        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId);
    }
}
