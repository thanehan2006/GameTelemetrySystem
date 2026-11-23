using System.Text.Json.Serialization;

namespace GameTelemetryAPI.Models
{
    // Lưu thông tin phiên chơi kèm các chỉ số chiến đấu và kinh tế tổng hợp
    public class GameSession
    {
        public long SessionId { get; set; }
        public int PlayerId { get; set; }
        public int? LevelId { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public string? Result { get; set; }

        public int EnemiesKilled { get; set; }
        public bool BossKilled { get; set; }
        public int GoldEarned { get; set; }
        public int GoldSpent { get; set; }
        public int BlockHits { get; set; }
        public int BlockedDamage { get; set; }
        public int PerfectParries { get; set; }
        public int TrapHits { get; set; }
        public int TrapDamage { get; set; }

        [JsonIgnore]
        public Player? Player { get; set; }

        [JsonIgnore]
        public Level? Level { get; set; }

        [JsonIgnore]
        public ICollection<GameEvent> GameEvents { get; set; } = new List<GameEvent>();

        [JsonIgnore]
        public ICollection<ShopPurchase> ShopPurchases { get; set; } = new List<ShopPurchase>();
    }
}
