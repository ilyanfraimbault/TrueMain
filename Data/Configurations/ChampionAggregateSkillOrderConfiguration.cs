using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregateSkillOrderConfiguration : IEntityTypeConfiguration<ChampionAggregateSkillOrder>
{
    public void Configure(EntityTypeBuilder<ChampionAggregateSkillOrder> entity)
    {
        entity.ToTable("champion_aggregate_skill_orders");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ScopeId).IsRequired();
        entity.Property(e => e.SkillOrderKey).IsRequired().HasMaxLength(32);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();

        entity.HasIndex(e => new { e.ScopeId, e.SkillOrderKey }).IsUnique();
        entity.HasIndex(e => e.ScopeId);

        entity.HasOne(e => e.Scope)
            .WithMany(s => s.SkillOrders)
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
