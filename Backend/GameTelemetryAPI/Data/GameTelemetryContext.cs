using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Data
{
    // DbContext quản lý kết nối và ánh xạ các bảng dữ liệu telemetry với SQL Server
    public class GameTelemetryContext : DbContext
    {
        public GameTelemetryContext(DbContextOptions<GameTelemetryContext> options)
            : base(options)
        {
        }

        // Bảng lưu tài khoản người chơi
        public DbSet<Player> Players { get; set; } = null!;
        // Bảng lưu các phiên chơi (session)
        public DbSet<GameSession> GameSessions { get; set; } = null!;
        // Bảng lưu chi tiết sự kiện trong từng phiên chơi
        public DbSet<GameEvent> GameEvents { get; set; } = null!;

        // Cấu hình khóa chính, ràng buộc dữ liệu và quan hệ giữa Players, GameSessions, GameEvents
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
                    .HasDefaultValueSql("SYSDATETIME()");
            });

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
