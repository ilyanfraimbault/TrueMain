using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimSkillOrderConfiguration : IEntityTypeConfiguration<ChampionDimSkillOrder>
{
    public void Configure(EntityTypeBuilder<ChampionDimSkillOrder> entity)
    {
        entity.ToTable("champion_dim_skill_orders");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.SkillOrderKey).IsRequired().HasMaxLength(64);

        entity.HasIndex(e => e.SkillOrderKey).IsUnique();
    }
}
