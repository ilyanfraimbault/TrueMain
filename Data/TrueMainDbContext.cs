using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class TrueMainDbContext : DbContext
{
    public TrueMainDbContext(DbContextOptions<TrueMainDbContext> options) : base(options)
    {
    }

    public DbSet<RiotAccount> RiotAccounts => Set<RiotAccount>();
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<ParticipantPerkSelection> ParticipantPerkSelections => Set<ParticipantPerkSelection>();
    public DbSet<MainCandidate> MainCandidates => Set<MainCandidate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RiotAccount>(entity =>
        {
            entity.ToTable("riot_accounts");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Puuid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.GameName)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.TagLine)
                .HasMaxLength(8);

            entity.Property(e => e.PlatformId)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(e => e.PersonaId);

            entity.Property(e => e.SummonerId)
                .HasMaxLength(128);

            entity.Property(e => e.ProfileIconId)
                .IsRequired();

            entity.Property(e => e.SummonerLevel)
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("now()");

            entity.Property(e => e.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("now()");

            entity.Property(e => e.LastProfileSyncAtUtc);

            entity.HasIndex(e => e.Puuid)
                .IsUnique();

            entity.HasIndex(e => e.PersonaId);

            entity.HasIndex(e => new { e.GameName, e.TagLine, e.PlatformId })
                .IsUnique();

            entity.HasOne(e => e.Persona)
                .WithMany(p => p.RiotAccounts)
                .HasForeignKey(e => e.PersonaId);
        });

        modelBuilder.Entity<Persona>(entity =>
        {
            entity.ToTable("personas");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.DisplayName)
                .HasMaxLength(64);

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<MainCandidate>(entity =>
        {
            entity.ToTable("main_candidates");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.PlatformId)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(e => e.Puuid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.ChampionId)
                .IsRequired();

            entity.Property(e => e.ChampionRankInMasteryTop)
                .IsRequired();

            entity.Property(e => e.ChampionPoints)
                .IsRequired();

            entity.Property(e => e.LastPlayTimeUtc)
                .IsRequired();

            entity.Property(e => e.DiscoveredAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.PlatformId, e.Puuid, e.ChampionId })
                .IsUnique();

            entity.HasIndex(e => e.PlatformId);

            entity.HasIndex(e => e.ChampionId);
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

            entity.Property(e => e.ItemEvents)
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(e => e.SkillEvents)
                .HasColumnType("jsonb")
                .IsRequired();
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
