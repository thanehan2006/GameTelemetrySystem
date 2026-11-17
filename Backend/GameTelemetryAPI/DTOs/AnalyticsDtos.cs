namespace GameTelemetryAPI.DTOs
{
    public class OverviewStatsDto
    {
        public int TotalPlayers { get; set; }
        public int TotalSessions { get; set; }
        public int TotalEvents { get; set; }
        public double AverageSessionDurationMinutes { get; set; }
    }

    public class EventCountDto
    {
        public string EventType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class LevelStatsDto
    {
        public int Level { get; set; }
        public int Starts { get; set; }
        public int Completions { get; set; }
        public int Deaths { get; set; }
        public double CompletionRatePercent { get; set; }
    }

    public class SessionsByDateDto
    {
        public string Date { get; set; } = string.Empty;
        public int SessionCount { get; set; }
    }

    public class RecentSessionDto
    {
        public long SessionId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? DurationMinutes { get; set; }
        public string? Result { get; set; }
        public int TotalEvents { get; set; }
    }
}
