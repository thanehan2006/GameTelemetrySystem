namespace GameTelemetryAPI.DTOs
{
    // DTO chứa các chỉ số tổng quan cho dashboard phân tích
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

    // DTO lưu số lần xuất hiện theo từng loại sự kiện
    public class EventCountDto
    {
        public string EventType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // DTO thống kê lượt chơi, tỷ lệ thắng và thất bại theo từng màn
    public class LevelStatsDto
    {
        public int Level { get; set; }
        public string StageName { get; set; } = string.Empty;
        public int Starts { get; set; }
        public int Completions { get; set; }
        public int Deaths { get; set; }
        public double CompletionRatePercent { get; set; }
    }

    // DTO thống kê số phiên chơi theo từng ngày
    public class SessionsByDateDto
    {
        public string Date { get; set; } = string.Empty;
        public int SessionCount { get; set; }
    }

    // DTO thông tin tóm tắt của một phiên chơi gần đây
    public class RecentSessionDto
    {
        public long SessionId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string? CharacterId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public int? Level { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? DurationMinutes { get; set; }
        public string? Result { get; set; }
        public int TotalEvents { get; set; }
        public int EnemiesKilled { get; set; }
        public int GoldEarned { get; set; }
        public int GoldSpent { get; set; }
        public int PerfectParries { get; set; }
    }

    // DTO thống kê tỷ lệ chọn và tỷ lệ thắng theo từng nhân vật cho dashboard
    public class CharacterAnalyticsStatDto
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string RoleBadge { get; set; } = string.Empty;
        public string AccentHex { get; set; } = "#f2bd47";
        public int TotalSessions { get; set; }
        public double PickRatePercent { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double WinRatePercent { get; set; }
        public double AvgEnemiesKilled { get; set; }
        public int BossKills { get; set; }
        public double AvgDurationMinutes { get; set; }
    }

    // DTO phản hồi danh sách phiên chơi có phân trang cho lịch sử dashboard
    public class PagedSessionsResponseDto
    {
        public List<RecentSessionDto> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
    }

    // DTO thống kê lượt mua và doanh thu vàng theo từng vũ khí
    public class WeaponPurchaseStatDto
    {
        public int Price { get; set; }
        public string WeaponName { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public int BonusDamage { get; set; }
        public int PurchaseCount { get; set; }
        public int TotalGoldSpent { get; set; }
    }

    // DTO tổng hợp dòng chảy vàng, bình máu và vật phẩm cửa hàng
    public class EconomyStatsDto
    {
        public int TotalGoldCollected { get; set; }
        public int TotalGoldSpent { get; set; }
        public int GoldSpentOnWeapons { get; set; }
        public int GoldSpentOnPotions { get; set; }
        public double SpendRatioPercent { get; set; }
        public double AvgGoldPerSession { get; set; }
        public int WeaponPurchaseCount { get; set; }
        public int PotionPurchaseCount { get; set; }
        public List<WeaponPurchaseStatDto> WeaponBreakdown { get; set; } = new();
    }

    // DTO tổng hợp các chỉ số chiến đấu như hạ quái, boss, đỡ đòn và bẫy
    public class CombatStatsDto
    {
        public int TotalEnemyKills { get; set; }
        public int BossKills { get; set; }
        public int TotalSessions { get; set; }
        public double BossClearRatePercent { get; set; }
        public int BlockHits { get; set; }
        public int TotalBlockedDamage { get; set; }
        public int PerfectParries { get; set; }
        public double ParryMasteryPercent { get; set; }
        public int TrapHits { get; set; }
        public int TotalTrapDamage { get; set; }
    }

    // DTO biểu diễn mốc thời gian chi tiết của một sự kiện trong phiên chơi
    public class SessionEventTimelineDto
    {
        public long EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime EventTime { get; set; }
        public int? Value { get; set; }
        public double OffsetSeconds { get; set; }
    }
}
