// h
using System.Text.Json.Serialization;

// u
namespace GameTelemetryAPI.Models
{
    public class GameEvent
    {
        public long EventId { get; set; }
        public long SessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime EventTime { get; set; } = DateTime.UtcNow;
        public int? Value { get; set; }
        public string? ClientEventId { get; set; }

        [JsonIgnore]
        public GameSession? GameSession { get; set; }
    }
}
