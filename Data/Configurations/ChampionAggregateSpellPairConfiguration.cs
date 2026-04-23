using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregateSpellPairConfiguration : IEntityTypeConfiguration<ChampionAggregateSpellPair>
{
    public void Configure(EntityTypeBuilder<ChampionAggregateSpellPair> entity)
    {
        entity.ToTable("champion_aggregate_spell_pairs");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ScopeId).IsRequired();
        entity.Property(e => e.Spell1Id).IsRequired();
        entity.Property(e => e.Spell2Id).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();

        entity.HasIndex(e => new { e.ScopeId, e.Spell1Id, e.Spell2Id }).IsUnique();
        entity.HasIndex(e => e.ScopeId);

        entity.HasOne(e => e.Scope)
            .WithMany(s => s.SpellPairs)
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
