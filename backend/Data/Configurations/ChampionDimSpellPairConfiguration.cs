using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimSpellPairConfiguration : IEntityTypeConfiguration<ChampionDimSpellPair>
{
    public void Configure(EntityTypeBuilder<ChampionDimSpellPair> entity)
    {
        entity.ToTable("champion_dim_spell_pairs");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.Spell1Id).IsRequired();
        entity.Property(e => e.Spell2Id).IsRequired();

        entity.HasIndex(e => new { e.Spell1Id, e.Spell2Id }).IsUnique();
    }
}
