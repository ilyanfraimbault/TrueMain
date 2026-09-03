using Data.DataQuality;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimSpellPairConfiguration : IEntityTypeConfiguration<ChampionDimSpellPair>
{
    public void Configure(EntityTypeBuilder<ChampionDimSpellPair> entity)
    {
        // The pair is a set, so its identity is the sorted pair, not the stored columns.
        // The UNIQUE index over that expression lives in the migration — EF cannot model
        // an expression index — and the CHECK is what keeps the stored order canonical,
        // so a writer regression fails on the spot instead of splitting the dimension
        // silently (#911, #1418).
        entity.ToTable(
            "champion_dim_spell_pairs",
            table => table.HasCheckConstraint(
                ChampionDimensionCanonicalKeys.SpellPairCanonicalCheckName,
                ChampionDimensionCanonicalKeys.SpellPairCanonicalCheck));

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.Spell1Id).IsRequired();
        entity.Property(e => e.Spell2Id).IsRequired();
    }
}
