using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchParticipantConfiguration : IEntityTypeConfiguration<MatchParticipant>
{
    public void Configure(EntityTypeBuilder<MatchParticipant> entity)
    {
        entity.ToTable("match_participants");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ParticipantId)
            .IsRequired();

        entity.Property(e => e.Puuid)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(e => e.RiotAccountId);

        entity.Property(e => e.SummonerName)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.SummonerLevel)
            .IsRequired();

        entity.Property(e => e.ChampionId)
            .IsRequired();

        entity.Property(e => e.TeamId)
            .IsRequired();

        entity.Property(e => e.TeamPosition)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.IndividualPosition)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.Lane)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.Win)
            .IsRequired();

        entity.Property(e => e.Kills)
            .IsRequired();

        entity.Property(e => e.Deaths)
            .IsRequired();

        entity.Property(e => e.Assists)
            .IsRequired();

        entity.Property(e => e.TotalDamageDealtToChampions)
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(e => e.VisionScore)
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(e => e.GoldEarned)
            .IsRequired();

        entity.Property(e => e.TotalMinionsKilled)
            .IsRequired();

        entity.Property(e => e.NeutralMinionsKilled)
            .IsRequired();

        entity.Property(e => e.ChampLevel)
            .IsRequired();

        entity.Property(e => e.Item0)
            .IsRequired();
        entity.Property(e => e.Item1)
            .IsRequired();
        entity.Property(e => e.Item2)
            .IsRequired();
        entity.Property(e => e.Item3)
            .IsRequired();
        entity.Property(e => e.Item4)
            .IsRequired();
        entity.Property(e => e.Item5)
            .IsRequired();
        entity.Property(e => e.Item6)
            .IsRequired();

        entity.Property(e => e.TrinketItemId)
            .IsRequired();

        entity.Property(e => e.PerksDefense)
            .IsRequired();
        entity.Property(e => e.PerksFlex)
            .IsRequired();
        entity.Property(e => e.PerksOffense)
            .IsRequired();
        entity.Property(e => e.PrimaryStyleId)
            .IsRequired();
        entity.Property(e => e.SubStyleId)
            .IsRequired();

        entity.Property(e => e.Summoner1Id)
            .IsRequired();
        entity.Property(e => e.Summoner2Id)
            .IsRequired();

        entity.Property(e => e.ItemEvents)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(e => e.SkillEvents)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.HasIndex(e => new { e.Puuid, e.MatchId })
            .HasDatabaseName("IX_match_participants_puuid_match");

        entity.HasIndex(e => new { e.MatchId, e.ParticipantId })
            .IsUnique();

        entity.HasIndex(e => e.RiotAccountId);

        // The champion-page reads (builds, matchups, scaling, leads, item-timings,
        // roam) all filter the tracked-account rows by champion + lane. A partial
        // index on those columns (only the tracked rows, ~1/10 of the table) turns
        // those filters into an index seek instead of a scan of the full 35 GB
        // match_participants table.
        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition })
            .HasFilter("\"RiotAccountId\" IS NOT NULL")
            .HasDatabaseName("IX_match_participants_champion_position_tracked");

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId);
    }
}
