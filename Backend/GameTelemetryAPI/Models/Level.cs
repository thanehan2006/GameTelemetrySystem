using System.Text.Json.Serialization;

namespace GameTelemetryAPI.Models
{
    // Danh mục các bản đồ chiến dịch (1 Map = 1 Level)
    public class Level
    {
        public int LevelId { get; set; }
        public string LevelName { get; set; } = string.Empty;
        public string BossName { get; set; } = string.Empty;
        public decimal DifficultyMult { get; set; } = 1.00m;

        [JsonIgnore]
        public ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
    }
}
