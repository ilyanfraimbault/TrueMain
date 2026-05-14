using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregatePatternConfiguration : IEntityTypeConfiguration<ChampionAggregatePattern>
{
    public void Configure(EntityTypeBuilder<ChampionAggregatePattern> entity)
    {
        entity.ToTable("champion_aggregate_patterns");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ScopeId).IsRequired();
        entity.Property(e => e.BuildId).IsRequired();
        entity.Property(e => e.RunePageId).IsRequired();
        entity.Property(e => e.SkillOrderId).IsRequired();
        entity.Property(e => e.SpellPairId).IsRequired();
        entity.Property(e => e.StarterItemsId).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();

        // The natural key of a pattern is (scope, build, runes, skill, spells,
        // starters). UNIQUE on this tuple gives us idempotent INSERT ...
        // ON CONFLICT (...) DO UPDATE for the get-or-create write path in 6.2.
        entity.HasIndex(e => new
        {
            e.ScopeId,
            e.BuildId,
            e.RunePageId,
            e.SkillOrderId,
            e.SpellPairId,
            e.StarterItemsId
        }).IsUnique();

        // Per-pivot indexes for the read-side correlation queries
        // ("for build X, what runes / skills / spells / starters") that
        // PR 6.3 will introduce. ScopeId leads each one because every
        // correlation query is scope-bound.
        entity.HasIndex(e => new { e.ScopeId, e.BuildId });
        entity.HasIndex(e => new { e.ScopeId, e.RunePageId });
        entity.HasIndex(e => new { e.ScopeId, e.SkillOrderId });
        entity.HasIndex(e => new { e.ScopeId, e.SpellPairId });
        entity.HasIndex(e => new { e.ScopeId, e.StarterItemsId });

        // Scope cascade: deleting a scope drops its patterns. Mirrors the
        // existing legacy aggregates' delete semantics (replace-by-scope).
        entity.HasOne(e => e.Scope)
            .WithMany()
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dim references are restrict: a dim row cannot be deleted while
        // any pattern still points at it. In steady state dims are
        // append-only, so this should never fire — it's a guard against
        // accidental cleanup that would orphan patterns.
        entity.HasOne(e => e.Build)
            .WithMany()
            .HasForeignKey(e => e.BuildId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(e => e.RunePage)
            .WithMany()
            .HasForeignKey(e => e.RunePageId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(e => e.SkillOrder)
            .WithMany()
            .HasForeignKey(e => e.SkillOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(e => e.SpellPair)
            .WithMany()
            .HasForeignKey(e => e.SpellPairId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(e => e.StarterItems)
            .WithMany()
            .HasForeignKey(e => e.StarterItemsId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
