using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class TrueMainDbContext : DbContext
{
    public TrueMainDbContext(DbContextOptions<TrueMainDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<ParticipantItemEvent> ParticipantItemEvents => Set<ParticipantItemEvent>();
    public DbSet<ParticipantSkillEvent> ParticipantSkillEvents => Set<ParticipantSkillEvent>();
    public DbSet<ParticipantPerkSelection> ParticipantPerkSelections => Set<ParticipantPerkSelection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("players");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.GameName)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.TagLine)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(e => e.Region)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(e => e.Puuid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("now()");

            entity.HasIndex(e => e.Puuid)
                .IsUnique();

            entity.HasIndex(e => new { e.GameName, e.TagLine, e.Region })
                .IsUnique();
        });

        modelBuilder.Entity<MatchParticipant>(entity =>
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
        });

        modelBuilder.Entity<ParticipantItemEvent>(entity =>
        {
            entity.ToTable("participant_item_events");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.MatchParticipantId)
                .IsRequired();

            entity.Property(e => e.MatchId)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.ParticipantId)
                .IsRequired();

            entity.Property(e => e.Puuid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.TimestampMs)
                .IsRequired();

            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.ItemId)
                .IsRequired();

            entity.Property(e => e.BeforeId);

            entity.Property(e => e.AfterId);

            entity.HasOne(e => e.MatchParticipant)
                .WithMany(p => p.ItemEvents)
                .HasForeignKey(e => e.MatchParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParticipantSkillEvent>(entity =>
        {
            entity.ToTable("participant_skill_events");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.MatchParticipantId)
                .IsRequired();

            entity.Property(e => e.MatchId)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.ParticipantId)
                .IsRequired();

            entity.Property(e => e.Puuid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.TimestampMs)
                .IsRequired();

            entity.Property(e => e.SkillSlot)
                .IsRequired();

            entity.Property(e => e.LevelUpType)
                .IsRequired()
                .HasMaxLength(32);

            entity.HasOne(e => e.MatchParticipant)
                .WithMany(p => p.SkillEvents)
                .HasForeignKey(e => e.MatchParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParticipantPerkSelection>(entity =>
        {
            entity.ToTable("participant_perk_selections");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.MatchId)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.ParticipantId)
                .IsRequired();

            entity.Property(e => e.StyleId)
                .IsRequired();

            entity.Property(e => e.StyleDescription)
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(e => e.SelectionIndex)
                .IsRequired();

            entity.Property(e => e.PerkId)
                .IsRequired();

            entity.HasIndex(e => new { e.MatchId, e.ParticipantId, e.StyleId, e.SelectionIndex })
                .IsUnique();
        });
    }
}
