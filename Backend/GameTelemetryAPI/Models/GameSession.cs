// h
using System.Text.Json.Serialization;

// u
namespace GameTelemetryAPI.Models
{
    public class GameSession
    {
        public long SessionId { get; set; }
        public int PlayerId { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public string? Result { get; set; }

        [JsonIgnore]
        public Player? Player { get; set; }

        [JsonIgnore]
        public ICollection<GameEvent> GameEvents { get; set; } = new List<GameEvent>();
    }
}
