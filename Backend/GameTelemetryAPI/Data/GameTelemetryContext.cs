using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Data
{
    // DbContext quản lý kết nối và ánh xạ hệ thống 5 bảng dữ liệu telemetry với SQL Server
    public class GameTelemetryContext : DbContext
    {
        public GameTelemetryContext(DbContextOptions<GameTelemetryContext> options)
            : base(options)
        {
        }

        public DbSet<Player> Players { get; set; } = null!;
        public DbSet<Level> Levels { get; set; } = null!;
        public DbSet<GameSession> GameSessions { get; set; } = null!;
        public DbSet<ShopPurchase> ShopPurchases { get; set; } = null!;
        public DbSet<GameEvent> GameEvents { get; set; } = null!;

        // Cấu hình khóa chính, ràng buộc dữ liệu và quan hệ giữa 5 bảng
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
                    .HasDefaultValueSql("SYSUTCDATETIME()");
            });

            modelBuilder.Entity<Level>(entity =>
            {
                entity.ToTable("Levels");
                entity.HasKey(e => e.LevelId);
                entity.Property(e => e.LevelId).ValueGeneratedNever();

                entity.Property(e => e.LevelName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.BossName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.DifficultyMult)
                    .HasColumnType("decimal(4,2)")
                    .HasDefaultValue(1.00m);
            });

            modelBuilder.Entity<GameSession>(entity =>
            {
                entity.ToTable("GameSessions");
                entity.HasKey(e => e.SessionId);

                entity.Property(e => e.StartTime)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.Property(e => e.Result)
                    .HasMaxLength(20);

                entity.HasOne(e => e.Player)
                    .WithMany(p => p.GameSessions)
                    .HasForeignKey(e => e.PlayerId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_GameSessions_Players");

                entity.HasOne(e => e.Level)
                    .WithMany(l => l.GameSessions)
                    .HasForeignKey(e => e.LevelId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .HasConstraintName("FK_GameSessions_Levels");
            });

            modelBuilder.Entity<ShopPurchase>(entity =>
            {
                entity.ToTable("ShopPurchases");
                entity.HasKey(e => e.PurchaseId);

                entity.Property(e => e.ItemType)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.ItemName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Rarity)
                    .HasMaxLength(30);

                entity.Property(e => e.PurchasedAt)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.HasOne(e => e.GameSession)
                    .WithMany(s => s.ShopPurchases)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_ShopPurchases_GameSessions");
            });

            modelBuilder.Entity<GameEvent>(entity =>
            {
                entity.ToTable("GameEvents");
                entity.HasKey(e => e.EventId);

                entity.Property(e => e.EventType)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.EventTime)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.HasOne(e => e.GameSession)
                    .WithMany(s => s.GameEvents)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_GameEvents_GameSessions");
            });
        }
    }
}
