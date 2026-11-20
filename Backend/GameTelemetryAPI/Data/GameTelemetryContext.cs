using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Data
{
    public class GameTelemetryContext : DbContext
    {
        public GameTelemetryContext(DbContextOptions<GameTelemetryContext> options)
            : base(options)
        {
        }

        public DbSet<Player> Players { get; set; } = null!;
        public DbSet<GameSession> GameSessions { get; set; } = null!;
        public DbSet<GameEvent> GameEvents { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình bảng Players
            modelBuilder.Entity<Player>(entity =>
            {
                entity.ToTable("Players");
                entity.HasKey(e => e.PlayerId);

                entity.Property(e => e.PlayerName)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.PasswordHash)
                    .IsRequired()
                    .HasMaxLength(256)
                    .HasDefaultValue(string.Empty);

                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("SYSDATETIME()");
            });

            // Cấu hình bảng GameSessions
            modelBuilder.Entity<GameSession>(entity =>
            {
                entity.ToTable("GameSessions");
                entity.HasKey(e => e.SessionId);

                entity.Property(e => e.StartTime)
                    .HasDefaultValueSql("SYSDATETIME()");

                entity.Property(e => e.Result)
                    .HasMaxLength(20);

                entity.HasOne(e => e.Player)
                    .WithMany(p => p.GameSessions)
                    .HasForeignKey(e => e.PlayerId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_GameSessions_Players");
            });

            // Cấu hình bảng GameEvents
            modelBuilder.Entity<GameEvent>(entity =>
            {
                entity.ToTable("GameEvents");
                entity.HasKey(e => e.EventId);

                entity.Property(e => e.EventType)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.EventTime)
                    .HasDefaultValueSql("SYSDATETIME()");

                entity.HasOne(e => e.GameSession)
                    .WithMany(s => s.GameEvents)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_GameEvents_GameSessions");
            });
        }
    }
}
