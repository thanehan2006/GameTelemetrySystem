using System.Text.Json.Serialization;

namespace GameTelemetryAPI.Models
{
    public class Player
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;

        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
    }
}
