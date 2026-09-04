using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionProfileStatConfiguration : IEntityTypeConfiguration<ChampionProfileStat>
{
    public void Configure(EntityTypeBuilder<ChampionProfileStat> entity)
    {
        entity.ToTable("champion_profile_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);

        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.GameDurationSecondsSum).IsRequired();
        entity.Property(e => e.PhysicalDamageToChampionsSum).IsRequired();
        entity.Property(e => e.MagicDamageToChampionsSum).IsRequired();
        entity.Property(e => e.TrueDamageToChampionsSum).IsRequired();
        entity.Property(e => e.TotalHealSum).IsRequired();
        entity.Property(e => e.HealsOnTeammatesSum).IsRequired();
        entity.Property(e => e.DamageShieldedOnTeammatesSum).IsRequired();
        entity.Property(e => e.TimeCCingOthersSum).IsRequired();
        entity.Property(e => e.TotalTimeCCDealtSum).IsRequired();
        entity.Property(e => e.DamageTakenSum).IsRequired();
        entity.Property(e => e.DamageSelfMitigatedSum).IsRequired();
        entity.Property(e => e.TeamDamageTakenGames).IsRequired();
        entity.Property(e => e.TeamDamageTakenSum).IsRequired();
        entity.Property(e => e.LaneGamesAt10).IsRequired();
        entity.Property(e => e.GoldLeadAt10Sum).IsRequired();
        entity.Property(e => e.XpLeadAt10Sum).IsRequired();
        entity.Property(e => e.KillsBy10Sum).IsRequired();
        entity.Property(e => e.LaneGamesAt15).IsRequired();
        entity.Property(e => e.GoldLeadAt15Sum).IsRequired();
        entity.Property(e => e.XpLeadAt15Sum).IsRequired();
        entity.Property(e => e.ItemGames).IsRequired();
        entity.Property(e => e.CritGames).IsRequired();
        entity.Property(e => e.ArmorPenetrationGames).IsRequired();
        entity.Property(e => e.OnHitGames).IsRequired();
        entity.Property(e => e.AbilityPowerGames).IsRequired();
        entity.Property(e => e.TankGames).IsRequired();
        entity.Property(e => e.IsRanged).IsRequired(false);
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain + the ON CONFLICT target of the incremental upsert.
        // Patch leads because the situational fold (#1450) reads every champion's
        // profile for one patch to qualify a draft — one range scan rather than a seek
        // per co-participant.
        entity.HasIndex(e => new
        {
            e.Patch,
            e.ChampionId,
            e.Position,
        }).IsUnique().HasDatabaseName("IX_champion_profile_stats_grain");
    }
}
