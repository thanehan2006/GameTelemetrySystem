using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Controller cung cấp các API tổng hợp dữ liệu phân tích cho dashboard
    public class AnalyticsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        private static readonly Dictionary<int, string> StageNames = new()
        {
            { 1, "Màn 1: Trại Rừng Ngoại Vi" },
            { 2, "Màn 2: Thung Lũng Bẫy" },
            { 3, "Màn 3: Đấu Trường Mỏ Vàng" }
        };

        private static readonly (int Price, string Name, string Rarity)[] WeaponCatalog =
        {
            (60,  "Kiếm 2",      "Hiếm"),
            (100, "Kiếm 3",      "Hiếm"),
            (150, "Búa Chiến",   "Sử Thi"),
            (210, "Kiếm Đỏ",     "Sử Thi"),
            (280, "Kiếm Xanh",   "Huyền Thoại"),
            (360, "Thương Vàng", "Huyền Thoại"),
            (450, "Búa Đỏ",      "Thần Thoại"),
            (550, "Đại Kiếm Hoàng Kim", "Thần Thoại")
        };

        public AnalyticsController(GameTelemetryContext context)
        {
            _context = context;
        }

        // Lấy các chỉ số tổng quan như tổng người chơi, phiên chơi, tỷ lệ thắng và thời lượng trung bình
        [HttpGet("overview")]
        public async Task<ActionResult<OverviewStatsDto>> GetOverviewStats()
        {
            var totalPlayers = await _context.Players.CountAsync();
            var sessions = await _context.GameSessions
                .Select(s => new { s.StartTime, s.EndTime, s.Result })
                .ToListAsync();

            var totalSessions = sessions.Count;
            var totalEvents = await _context.GameEvents.CountAsync();

            var finishedSessions = sessions.Where(s => s.EndTime != null).ToList();
            double avgDuration = finishedSessions.Count > 0
                ? finishedSessions.Average(s => (s.EndTime!.Value - s.StartTime).TotalMinutes)
                : 0;

            int totalWins = sessions.Count(s => IsWinResult(s.Result));
            int totalLosses = sessions.Count(s => IsLoseResult(s.Result));
            int decidedCount = totalWins + totalLosses;
            double winRate = decidedCount > 0
                ? Math.Round((double)totalWins / decidedCount * 100, 1)
                : 0;

            var summaryEvents = await _context.GameEvents
                .Where(e => e.EventType == "gold_collected" ||
                            e.EventType == "weapon_purchased" ||
                            e.EventType == "potion_purchased" ||
                            e.EventType == "enemy_killed" ||
                            e.EventType == "boss_killed")
                .Select(e => new { e.EventType, Value = e.Value ?? 0 })
                .ToListAsync();

            int totalGoldEarned = summaryEvents
                .Where(e => e.EventType == "gold_collected")
                .Sum(e => e.Value);

            int totalGoldSpent = summaryEvents
                .Where(e => e.EventType == "weapon_purchased" || e.EventType == "potion_purchased")
                .Sum(e => e.Value);

            int totalEnemiesKilled = summaryEvents.Count(e => e.EventType == "enemy_killed");
            int totalBossKills = summaryEvents.Count(e => e.EventType == "boss_killed");

            return Ok(new OverviewStatsDto
            {
                TotalPlayers = totalPlayers,
                TotalSessions = totalSessions,
                TotalEvents = totalEvents,
                AverageSessionDurationMinutes = Math.Round(avgDuration, 2),
                TotalWins = totalWins,
                TotalLosses = totalLosses,
                ActiveOrQuitSessions = Math.Max(0, totalSessions - decidedCount),
                WinRatePercent = winRate,
                TotalGoldEarned = totalGoldEarned,
                TotalGoldSpent = totalGoldSpent,
                TotalEnemiesKilled = totalEnemiesKilled,
                TotalBossKills = totalBossKills
            });
        }

        // Thống kê tiến độ, tỷ lệ qua màn và số lượt thất bại theo từng màn
        [HttpGet("levels")]
        public async Task<ActionResult<List<LevelStatsDto>>> GetLevelStats([FromQuery] int? level = null)
        {
            var query = _context.GameEvents.AsQueryable();

            if (level.HasValue)
            {
                query = query.Where(e => e.Value == level.Value);
            }

            var events = await query
                .Where(e => e.Value != null &&
                            (e.EventType == "level_start" ||
                             e.EventType == "level_complete" ||
                             e.EventType == "player_dead" ||
                             e.EventType == "checkpoint_reached" ||
                             e.EventType == "shop_opened" ||
                             e.EventType == "perfect_parry"))
                .Select(e => new { Level = e.Value!.Value, e.EventType })
                .ToListAsync();

            var grouped = events
                .GroupBy(e => e.Level)
                .ToDictionary(g => g.Key, g => g.ToList());

            var stageNumbers = level.HasValue
                ? new List<int> { level.Value }
                : new List<int> { 1, 2, 3 }.Union(grouped.Keys.Where(k => k >= 1 && k <= 10)).OrderBy(k => k).ToList();

            var result = stageNumbers.Select(lvl =>
            {
                var list = grouped.ContainsKey(lvl) ? grouped[lvl] : null;
                int starts = list?.Count(x => x.EventType == "level_start") ?? 0;
                int completions = list?.Count(x => x.EventType == "level_complete") ?? 0;
                int deaths = list?.Count(x => x.EventType == "player_dead") ?? 0;
                int checkpoints = list?.Count(x => x.EventType == "checkpoint_reached") ?? 0;
                int shopOpens = list?.Count(x => x.EventType == "shop_opened") ?? 0;
                int parries = list?.Count(x => x.EventType == "perfect_parry") ?? 0;
                double rate = starts > 0 ? Math.Round((double)completions / starts * 100, 1) : 0;

                return new LevelStatsDto
                {
                    Level = lvl,
                    StageName = StageNames.TryGetValue(lvl, out var name) ? name : $"Màn {lvl}",
                    Starts = starts,
                    Completions = completions,
                    Deaths = deaths,
                    CompletionRatePercent = rate,
                    CheckpointsReached = checkpoints,
                    ShopOpens = shopOpens,
                    PerfectParries = parries
                };
            }).ToList();

            return Ok(result);
        }

        // Thống kê phân bố số lượng theo từng loại sự kiện telemetry
        [HttpGet("events-distribution")]
        public async Task<ActionResult<List<EventCountDto>>> GetEventsDistribution()
        {
            var result = await _context.GameEvents
                .GroupBy(e => e.EventType)
                .Select(g => new EventCountDto
                {
                    EventType = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            return Ok(result);
        }

        // Thống kê lượng phiên chơi theo từng ngày
        [HttpGet("sessions-by-date")]
        public async Task<ActionResult<List<SessionsByDateDto>>> GetSessionsByDate()
        {
            var sessions = await _context.GameSessions
                .Select(s => s.StartTime)
                .ToListAsync();

            var result = sessions
                .GroupBy(s => s.ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key)
                .Select(g => new SessionsByDateDto
                {
                    Date = g.Key,
                    SessionCount = g.Count()
                })
                .ToList();

            return Ok(result);
        }

        // Lấy danh sách các phiên chơi gần nhất kèm số lượng sự kiện
        [HttpGet("recent-sessions")]
        public async Task<ActionResult<List<RecentSessionDto>>> GetRecentSessions([FromQuery] int limit = 15)
        {
            var sessions = await _context.GameSessions
                .Include(s => s.Player)
                .Include(s => s.GameEvents)
                .OrderByDescending(s => s.StartTime)
                .Take(limit)
                .Select(s => new RecentSessionDto
                {
                    SessionId = s.SessionId,
                    PlayerName = s.Player != null ? s.Player.PlayerName : "Unknown",
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    DurationMinutes = s.EndTime != null ? Math.Round((s.EndTime.Value - s.StartTime).TotalMinutes, 2) : null,
                    Result = s.Result,
                    TotalEvents = s.GameEvents.Count
                })
                .ToListAsync();

            return Ok(sessions);
        }

        // Phân tích kinh tế vàng thu thập, chi tiêu nâng cấp vũ khí và mua bình máu
        [HttpGet("economy")]
        public async Task<ActionResult<EconomyStatsDto>> GetEconomyStats()
        {
            var totalSessions = await _context.GameSessions.CountAsync();

            var econEvents = await _context.GameEvents
                .Where(e => e.EventType == "gold_collected" ||
                            e.EventType == "shop_opened" ||
                            e.EventType == "weapon_purchased" ||
                            e.EventType == "potion_purchased")
                .Select(e => new { e.EventType, Value = e.Value ?? 0 })
                .ToListAsync();

            int totalGoldCollected = econEvents
                .Where(e => e.EventType == "gold_collected")
                .Sum(e => e.Value);

            var weaponEvents = econEvents.Where(e => e.EventType == "weapon_purchased").ToList();
            var potionEvents = econEvents.Where(e => e.EventType == "potion_purchased").ToList();

            int goldOnWeapons = weaponEvents.Sum(e => e.Value);
            int goldOnPotions = potionEvents.Sum(e => e.Value);
            int totalGoldSpent = goldOnWeapons + goldOnPotions;

            int shopOpens = econEvents.Count(e => e.EventType == "shop_opened");
            int weaponPurchases = weaponEvents.Count;
            int potionPurchases = potionEvents.Count;

            double spendRatio = totalGoldCollected > 0
                ? Math.Round((double)totalGoldSpent / totalGoldCollected * 100, 1)
                : 0;

            double avgGoldPerSession = totalSessions > 0
                ? Math.Round((double)totalGoldCollected / totalSessions, 1)
                : 0;

            double shopConversion = shopOpens > 0
                ? Math.Round((double)(weaponPurchases + potionPurchases) / shopOpens * 100, 1)
                : 0;

            var weaponBreakdown = WeaponCatalog.Select(w =>
            {
                int count = weaponEvents.Count(e => e.Value == w.Price);
                return new WeaponPurchaseStatDto
                {
                    Price = w.Price,
                    WeaponName = w.Name,
                    Rarity = w.Rarity,
                    PurchaseCount = count,
                    TotalGoldSpent = count * w.Price
                };
            }).ToList();

            return Ok(new EconomyStatsDto
            {
                TotalGoldCollected = totalGoldCollected,
                TotalGoldSpent = totalGoldSpent,
                GoldSpentOnWeapons = goldOnWeapons,
                GoldSpentOnPotions = goldOnPotions,
                SpendRatioPercent = spendRatio,
                AvgGoldPerSession = avgGoldPerSession,
                ShopOpenCount = shopOpens,
                WeaponPurchaseCount = weaponPurchases,
                PotionPurchaseCount = potionPurchases,
                ShopConversionPercent = shopConversion,
                WeaponBreakdown = weaponBreakdown
            });
        }

        // Tổng hợp chỉ số chiến đấu gồm hạ quái, hạ boss, đỡ đòn, parry và sát thương bẫy
        [HttpGet("combat")]
        public async Task<ActionResult<CombatStatsDto>> GetCombatStats()
        {
            var combatEvents = await _context.GameEvents
                .Where(e => e.EventType == "enemy_killed" ||
                            e.EventType == "boss_killed" ||
                            e.EventType == "level_start" ||
                            e.EventType == "block_hit" ||
                            e.EventType == "perfect_parry" ||
                            e.EventType == "trap_hit" ||
                            e.EventType == "item_pickup" ||
                            e.EventType == "checkpoint_reached")
                .Select(e => new { e.EventType, Value = e.Value ?? 0 })
                .ToListAsync();

            var enemyKills = combatEvents.Where(e => e.EventType == "enemy_killed").ToList();
            int bossKills = combatEvents.Count(e => e.EventType == "boss_killed");
            int stage3Starts = combatEvents.Count(e => e.EventType == "level_start" && e.Value == 3);

            var blocks = combatEvents.Where(e => e.EventType == "block_hit").ToList();
            int parries = combatEvents.Count(e => e.EventType == "perfect_parry");
            int totalDefenses = blocks.Count + parries;

            var traps = combatEvents.Where(e => e.EventType == "trap_hit").ToList();
            var pickups = combatEvents.Where(e => e.EventType == "item_pickup").ToList();
            int checkpoints = combatEvents.Count(e => e.EventType == "checkpoint_reached");

            return Ok(new CombatStatsDto
            {
                TotalEnemyKills = enemyKills.Count,
                TotalKillScore = enemyKills.Sum(e => e.Value),
                BossKills = bossKills,
                Stage3Starts = stage3Starts,
                BossClearRatePercent = stage3Starts > 0 ? Math.Round((double)bossKills / stage3Starts * 100, 1) : 0,
                BlockHits = blocks.Count,
                TotalBlockedDamage = blocks.Sum(e => e.Value),
                PerfectParries = parries,
                ParryMasteryPercent = totalDefenses > 0 ? Math.Round((double)parries / totalDefenses * 100, 1) : 0,
                TrapHits = traps.Count,
                TotalTrapDamage = traps.Sum(e => e.Value),
                HealthPickups = pickups.Count,
                TotalHealthHealed = pickups.Sum(e => e.Value),
                CheckpointsReached = checkpoints
            });
        }

        // Truy xuất chuỗi sự kiện theo dòng thời gian của một phiên chơi cụ thể
        [HttpGet("sessions/{sessionId:long}/events")]
        public async Task<ActionResult<List<SessionEventTimelineDto>>> GetSessionEvents(long sessionId)
        {
            var session = await _context.GameSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);

            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy Session #{sessionId}" });
            }

            var events = await _context.GameEvents
                .AsNoTracking()
                .Where(e => e.SessionId == sessionId)
                .OrderBy(e => e.EventTime)
                .ThenBy(e => e.EventId)
                .ToListAsync();

            var startTime = session.StartTime;
            var timeline = events.Select(e => new SessionEventTimelineDto
            {
                EventId = e.EventId,
                EventType = e.EventType,
                EventTime = e.EventTime,
                Value = e.Value,
                OffsetSeconds = Math.Max(0, Math.Round((e.EventTime - startTime).TotalSeconds, 1))
            }).ToList();

            return Ok(timeline);
        }

        // Kiểm tra kết quả phiên chơi thuộc nhóm thắng
        private static bool IsWinResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return false;
            var r = result.Trim().ToLowerInvariant();
            return r == "win" || r == "victory" || r == "completed" || r == "thắng";
        }

        // Kiểm tra kết quả phiên chơi thuộc nhóm thua
        private static bool IsLoseResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return false;
            var r = result.Trim().ToLowerInvariant();
            return r == "lose" || r == "loss" || r == "defeat" || r == "dead" || r == "thua";
        }
    }
}
