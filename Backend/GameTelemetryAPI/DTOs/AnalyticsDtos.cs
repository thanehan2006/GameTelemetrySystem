namespace GameTelemetryAPI.DTOs
{
    public class OverviewStatsDto
    {
        public int TotalPlayers { get; set; }
        public int TotalSessions { get; set; }
        public int TotalEvents { get; set; }
        public double AverageSessionDurationMinutes { get; set; }
        public int TotalWins { get; set; }
        public int TotalLosses { get; set; }
        public int ActiveOrQuitSessions { get; set; }
        public double WinRatePercent { get; set; }
        public int TotalGoldEarned { get; set; }
        public int TotalGoldSpent { get; set; }
        public int TotalEnemiesKilled { get; set; }
        public int TotalBossKills { get; set; }
    }

    public class EventCountDto
    {
        public string EventType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class LevelStatsDto
    {
        public int Level { get; set; }
        public string StageName { get; set; } = string.Empty;
        public int Starts { get; set; }
        public int Completions { get; set; }
        public int Deaths { get; set; }
        public double CompletionRatePercent { get; set; }
        public int CheckpointsReached { get; set; }
        public int ShopOpens { get; set; }
        public int PerfectParries { get; set; }
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

    public class WeaponPurchaseStatDto
    {
        public int Price { get; set; }
        public string WeaponName { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public int PurchaseCount { get; set; }
        public int TotalGoldSpent { get; set; }
    }

    public class EconomyStatsDto
    {
        public int TotalGoldCollected { get; set; }
        public int TotalGoldSpent { get; set; }
        public int GoldSpentOnWeapons { get; set; }
        public int GoldSpentOnPotions { get; set; }
        public double SpendRatioPercent { get; set; }
        public double AvgGoldPerSession { get; set; }
        public int ShopOpenCount { get; set; }
        public int WeaponPurchaseCount { get; set; }
        public int PotionPurchaseCount { get; set; }
        public double ShopConversionPercent { get; set; }
        public List<WeaponPurchaseStatDto> WeaponBreakdown { get; set; } = new();
    }

    public class CombatStatsDto
    {
        public int TotalEnemyKills { get; set; }
        public int TotalKillScore { get; set; }
        public int BossKills { get; set; }
        public int Stage3Starts { get; set; }
        public double BossClearRatePercent { get; set; }
        public int BlockHits { get; set; }
        public int TotalBlockedDamage { get; set; }
        public int PerfectParries { get; set; }
        public double ParryMasteryPercent { get; set; }
        public int TrapHits { get; set; }
        public int TotalTrapDamage { get; set; }
        public int HealthPickups { get; set; }
        public int TotalHealthHealed { get; set; }
        public int CheckpointsReached { get; set; }
    }

    public class SessionEventTimelineDto
    {
        public long EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime EventTime { get; set; }
        public int? Value { get; set; }
        public double OffsetSeconds { get; set; }
    }
}
