using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;
using GameTelemetryAPI.Services;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Controller cung cấp các API tổng hợp dữ liệu phân tích cho dashboard từ schema 5 bảng chuẩn hóa
    public class AnalyticsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public AnalyticsController(GameTelemetryContext context)
        {
            _context = context;
        }

        private static TimeSpan ResolveUtcOffset(int? tzOffsetMinutes)
        {
            if (tzOffsetMinutes.HasValue)
            {
                int clamped = Math.Clamp(tzOffsetMinutes.Value, -720, 840);
                return TimeSpan.FromMinutes(clamped);
            }
            return TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        }

        private IQueryable<GameSession> FilterSessions(DateTime? from, DateTime? to, int? level, int? tzOffset = null)
        {
            var query = _context.GameSessions.AsNoTracking();
            var offset = ResolveUtcOffset(tzOffset);

            if (from.HasValue)
            {
                var fromStartUtc = DateTime.SpecifyKind(from.Value.Date - offset, DateTimeKind.Utc);
                query = query.Where(s => s.StartTime >= fromStartUtc);
            }
            if (to.HasValue)
            {
                var toExclusiveUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1) - offset, DateTimeKind.Utc);
                query = query.Where(s => s.StartTime < toExclusiveUtc);
            }
            if (level.HasValue)
            {
                int targetLevel = level.Value;
                query = query.Where(s => s.LevelId == targetLevel);
            }
            return query;
        }

        // Lấy các chỉ số tổng quan như tổng người chơi, phiên chơi, tỷ lệ thắng, vàng và quái hạ gục
        [HttpGet("overview")]
        public async Task<ActionResult<OverviewStatsDto>> GetOverviewStats(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null)
        {
            var sessionQuery = FilterSessions(from, to, level, tzOffset);

            var totalPlayers = (from.HasValue || to.HasValue || level.HasValue)
                ? await sessionQuery.Select(s => s.PlayerId).Distinct().CountAsync()
                : await _context.Players.CountAsync();

            var sessions = await sessionQuery
                .Select(s => new
                {
                    s.SessionId,
                    s.StartTime,
                    s.EndTime,
                    s.Result,
                    s.GoldEarned,
                    s.GoldSpent,
                    s.EnemiesKilled,
                    s.BossKilled
                })
                .ToListAsync();

            var sessionIds = sessions.Select(s => s.SessionId).ToList();
            var totalEvents = sessionIds.Count > 0
                ? await _context.GameEvents.AsNoTracking().CountAsync(e => sessionIds.Contains(e.SessionId))
                : 0;

            var totalSessions = sessions.Count;
            var finishedSessions = sessions.Where(s => s.EndTime != null).ToList();
            double avgDuration = finishedSessions.Count > 0
                ? finishedSessions.Average(s => Math.Max(0, (s.EndTime!.Value - s.StartTime).TotalMinutes))
                : 0;

            int totalWins = sessions.Count(s => IsWinResult(s.Result));
            int totalLosses = sessions.Count(s => IsLoseResult(s.Result));
            int decidedCount = totalWins + totalLosses;
            double winRate = decidedCount > 0
                ? Math.Round((double)totalWins / decidedCount * 100, 1)
                : 0;

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
                TotalGoldEarned = sessions.Sum(s => s.GoldEarned),
                TotalGoldSpent = sessions.Sum(s => s.GoldSpent),
                TotalEnemiesKilled = sessions.Sum(s => s.EnemiesKilled),
                TotalBossKills = sessions.Count(s => s.BossKilled)
            });
        }

        // Thống kê tiến độ, tỷ lệ hoàn thành và số lượt thất bại theo từng Level (1 Map = 1 Level)
        [HttpGet("levels")]
        public async Task<ActionResult<List<LevelStatsDto>>> GetLevelStats(
            [FromQuery] int? level = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? tzOffset = null)
        {
            var levelNames = await _context.Levels
                .AsNoTracking()
                .ToDictionaryAsync(l => l.LevelId, l => l.LevelName);

            var filteredSessionIds = FilterSessions(from, to, level, tzOffset).Select(s => s.SessionId);
            var eventQuery = _context.GameEvents
                .AsNoTracking()
                .Where(e => filteredSessionIds.Contains(e.SessionId));

            if (level.HasValue)
            {
                eventQuery = eventQuery.Where(e => e.Value == level.Value);
            }

            var events = await eventQuery
                .Where(e => e.Value != null &&
                            (e.EventType == "level_start" ||
                             e.EventType == "level_complete" ||
                             e.EventType == "level_failed" ||
                             e.EventType == "player_dead"))
                .Select(e => new { e.SessionId, Level = e.Value!.Value, e.EventType })
                .ToListAsync();

            var grouped = events
                .GroupBy(e => e.Level)
                .ToDictionary(g => g.Key, g => g.ToList());

            var stageNumbers = level.HasValue
                ? new List<int> { level.Value }
                : new List<int> { 1, 2, 3 };

            var result = stageNumbers.Select(lvl =>
            {
                var list = grouped.ContainsKey(lvl) ? grouped[lvl] : null;
                int starts = list?.Count(x => x.EventType == "level_start") ?? 0;
                int completions = list?.Count(x => x.EventType == "level_complete") ?? 0;
                int deaths = list != null
                    ? list.Where(x => x.EventType == "player_dead" || x.EventType == "level_failed")
                          .Select(x => x.SessionId)
                          .Distinct()
                          .Count()
                    : 0;
                double rate = starts > 0 ? Math.Min(100, Math.Round((double)completions / starts * 100, 1)) : 0;

                return new LevelStatsDto
                {
                    Level = lvl,
                    StageName = levelNames.TryGetValue(lvl, out var name) ? name : $"Level {lvl}",
                    Starts = starts,
                    Completions = completions,
                    Deaths = deaths,
                    CompletionRatePercent = rate
                };
            }).ToList();

            return Ok(result);
        }

        // Thống kê phân bố số lượng theo từng loại sự kiện cột mốc trong GameEvents
        [HttpGet("events-distribution")]
        public async Task<ActionResult<List<EventCountDto>>> GetEventsDistribution()
        {
            var result = await _context.GameEvents
                .AsNoTracking()
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
        public async Task<ActionResult<List<SessionsByDateDto>>> GetSessionsByDate(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null)
        {
            var offset = ResolveUtcOffset(tzOffset);
            var sessions = await FilterSessions(from, to, level, tzOffset)
                .Select(s => s.StartTime)
                .ToListAsync();

            var result = sessions
                .GroupBy(s => (s + offset).ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key)
                .Select(g => new SessionsByDateDto
                {
                    Date = g.Key,
                    SessionCount = g.Count()
                })
                .ToList();

            return Ok(result);
        }

        // Lấy danh sách các phiên chơi gần nhất kèm Level và số liệu tổng hợp
        [HttpGet("recent-sessions")]
        public async Task<ActionResult<List<RecentSessionDto>>> GetRecentSessions(
            [FromQuery] int limit = 15,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null)
        {
            int safeLimit = Math.Clamp(limit, 1, 500);
            var query = FilterSessions(from, to, level, tzOffset)
                .OrderByDescending(s => s.StartTime)
                .ThenByDescending(s => s.SessionId)
                .Take(safeLimit);

            var sessions = await ProjectRecentSessionsAsync(query);
            return Ok(sessions);
        }

        // Lấy lịch sử phiên chơi có phân trang, tìm kiếm, lọc kết quả và sắp xếp
        [HttpGet("sessions")]
        public async Task<ActionResult<PagedSessionsResponseDto>> GetSessionsHistory(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] string? sort = "newest")
        {
            pageSize = Math.Clamp(pageSize, 1, 5000);
            var query = FilterSessions(from, to, level, tzOffset);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string idCandidate = term.StartsWith("#") ? term.Substring(1).Trim() : term;
                bool hasSessionId = long.TryParse(idCandidate, out long parsedSessionId);

                if (hasSessionId)
                {
                    query = query.Where(s =>
                        s.SessionId == parsedSessionId ||
                        (s.Player != null && s.Player.PlayerName.Contains(term)));
                }
                else
                {
                    query = query.Where(s =>
                        s.Player != null && s.Player.PlayerName.Contains(term));
                }
            }

            string? st = status?.Trim().ToLowerInvariant();
            if (st == "win")
            {
                query = query.Where(s =>
                    s.Result != null && (
                        s.Result.Trim().ToLower() == "win" ||
                        s.Result.Trim().ToLower() == "victory" ||
                        s.Result.Trim().ToLower() == "completed" ||
                        s.Result.Trim().ToLower() == "thắng"));
            }
            else if (st == "lose")
            {
                query = query.Where(s =>
                    s.Result != null && (
                        s.Result.Trim().ToLower() == "lose" ||
                        s.Result.Trim().ToLower() == "loss" ||
                        s.Result.Trim().ToLower() == "defeat" ||
                        s.Result.Trim().ToLower() == "dead" ||
                        s.Result.Trim().ToLower() == "thua"));
            }
            else if (st == "other")
            {
                query = query.Where(s =>
                    s.Result == null || (
                        s.Result.Trim().ToLower() != "win" &&
                        s.Result.Trim().ToLower() != "victory" &&
                        s.Result.Trim().ToLower() != "completed" &&
                        s.Result.Trim().ToLower() != "thắng" &&
                        s.Result.Trim().ToLower() != "lose" &&
                        s.Result.Trim().ToLower() != "loss" &&
                        s.Result.Trim().ToLower() != "defeat" &&
                        s.Result.Trim().ToLower() != "dead" &&
                        s.Result.Trim().ToLower() != "thua"));
            }

            int totalCount = await query.CountAsync();
            int totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 1;
            int safePage = Math.Clamp(page, 1, totalPages);

            bool sortOldest = string.Equals(sort?.Trim(), "oldest", StringComparison.OrdinalIgnoreCase);
            IOrderedQueryable<GameSession> ordered = sortOldest
                ? query.OrderBy(s => s.StartTime).ThenBy(s => s.SessionId)
                : query.OrderByDescending(s => s.StartTime).ThenByDescending(s => s.SessionId);

            var pagedQuery = ordered
                .Skip((safePage - 1) * pageSize)
                .Take(pageSize);

            var sessions = await ProjectRecentSessionsAsync(pagedQuery);

            return Ok(new PagedSessionsResponseDto
            {
                Items = sessions,
                Page = safePage,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            });
        }

        // Chuyển truy vấn GameSession sang danh sách RecentSessionDto với thời gian UTC chuẩn hóa
        private static async Task<List<RecentSessionDto>> ProjectRecentSessionsAsync(IQueryable<GameSession> query)
        {
            var rawSessions = await query
                .Select(s => new
                {
                    s.SessionId,
                    PlayerName = s.Player != null ? s.Player.PlayerName : "Unknown",
                    s.CharacterId,
                    Level = s.LevelId,
                    s.StartTime,
                    s.EndTime,
                    s.Result,
                    TotalEvents = s.GameEvents.Count + s.ShopPurchases.Count,
                    s.EnemiesKilled,
                    s.GoldEarned,
                    s.GoldSpent,
                    s.PerfectParries
                })
                .ToListAsync();

            return rawSessions.Select(s =>
            {
                var charInfo = CharacterCatalog.Resolve(s.CharacterId);
                return new RecentSessionDto
                {
                    SessionId = s.SessionId,
                    PlayerName = s.PlayerName,
                    CharacterId = s.CharacterId,
                    CharacterName = charInfo.DisplayName,
                    Level = s.Level,
                    StartTime = DateTime.SpecifyKind(s.StartTime, DateTimeKind.Utc),
                    EndTime = s.EndTime.HasValue ? DateTime.SpecifyKind(s.EndTime.Value, DateTimeKind.Utc) : null,
                    DurationMinutes = s.EndTime.HasValue
                        ? Math.Round(Math.Max(0, (s.EndTime.Value - s.StartTime).TotalMinutes), 2)
                        : null,
                    Result = s.Result,
                    TotalEvents = s.TotalEvents,
                    EnemiesKilled = s.EnemiesKilled,
                    GoldEarned = s.GoldEarned,
                    GoldSpent = s.GoldSpent,
                    PerfectParries = s.PerfectParries
                };
            }).ToList();
        }

        // Thống kê tỷ lệ chọn, tỷ lệ thắng và hiệu suất chiến đấu theo từng lớp nhân vật
        [HttpGet("characters")]
        public async Task<ActionResult<List<CharacterAnalyticsStatDto>>> GetCharacterStats(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null)
        {
            var rawSessions = await FilterSessions(from, to, level, tzOffset)
                .Select(s => new
                {
                    s.CharacterId,
                    s.Result,
                    s.EnemiesKilled,
                    s.BossKilled,
                    s.StartTime,
                    s.EndTime
                })
                .ToListAsync();

            int totalSessions = rawSessions.Count;
            var buckets = CharacterCatalog.AggregateByCharacter(
                rawSessions.Select(s => new CharacterCatalog.CharacterSessionSample(
                    s.CharacterId,
                    s.Result,
                    s.EnemiesKilled,
                    s.BossKilled,
                    s.StartTime,
                    s.EndTime)));

            var result = buckets.Select(b => new CharacterAnalyticsStatDto
            {
                CharacterId = b.Info.Id,
                CharacterName = b.Info.DisplayName,
                Title = b.Info.Title,
                RoleBadge = b.Info.RoleBadge,
                AccentHex = b.Info.AccentHex,
                TotalSessions = b.Matches,
                PickRatePercent = totalSessions > 0 ? Math.Round((double)b.Matches / totalSessions * 100, 1) : 0,
                Wins = b.Wins,
                Losses = b.Losses,
                WinRatePercent = b.WinRatePercent,
                AvgEnemiesKilled = b.AvgEnemiesPerMatch,
                BossKills = b.BossKills,
                AvgDurationMinutes = b.AvgDurationMinutes
            }).ToList();

            return Ok(result);
        }

        // Phân tích kinh tế vàng từ GameSessions và chi tiết giao dịch từ ShopPurchases
        [HttpGet("economy")]
        public async Task<ActionResult<EconomyStatsDto>> GetEconomyStats(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null)
        {
            var sessionQuery = FilterSessions(from, to, level, tzOffset);
            var sessionStats = await sessionQuery
                .Select(s => new { s.SessionId, s.GoldEarned, s.GoldSpent })
                .ToListAsync();

            int totalSessions = sessionStats.Count;
            int totalGoldCollected = sessionStats.Sum(s => s.GoldEarned);
            int totalGoldSpent = sessionStats.Sum(s => s.GoldSpent);

            var sessionIds = sessionStats.Select(s => s.SessionId).ToList();
            var purchases = sessionIds.Count > 0
                ? await _context.ShopPurchases
                    .AsNoTracking()
                    .Where(p => sessionIds.Contains(p.SessionId))
                    .Select(p => new { p.ItemType, p.Price })
                    .ToListAsync()
                : new();

            var weaponPurchasesList = purchases.Where(p => p.ItemType == "Weapon").ToList();
            var potionPurchasesList = purchases.Where(p => p.ItemType == "Potion").ToList();

            int goldOnWeapons = weaponPurchasesList.Sum(p => p.Price);
            int goldOnPotions = potionPurchasesList.Sum(p => p.Price);

            double spendRatio = totalGoldCollected > 0
                ? Math.Round((double)totalGoldSpent / totalGoldCollected * 100, 1)
                : 0;

            double avgGoldPerSession = totalSessions > 0
                ? Math.Round((double)totalGoldCollected / totalSessions, 1)
                : 0;

            var weaponBreakdown = WeaponCatalog.Items.Select(w =>
            {
                var matched = weaponPurchasesList.Where(p => p.Price == w.Price).ToList();
                return new WeaponPurchaseStatDto
                {
                    Price = w.Price,
                    WeaponName = w.Name,
                    Rarity = w.Rarity,
                    BonusDamage = w.BonusDamage,
                    PurchaseCount = matched.Count,
                    TotalGoldSpent = matched.Sum(p => p.Price)
                };
            }).ToList();

            var catalogPrices = WeaponCatalog.Items.Select(w => w.Price).ToHashSet();
            var legacyWeapons = weaponPurchasesList.Where(p => !catalogPrices.Contains(p.Price)).ToList();
            if (legacyWeapons.Count > 0)
            {
                weaponBreakdown.Add(new WeaponPurchaseStatDto
                {
                    Price = 0,
                    WeaponName = "Vũ khí khác (bản cũ)",
                    Rarity = "Cũ",
                    BonusDamage = 0,
                    PurchaseCount = legacyWeapons.Count,
                    TotalGoldSpent = legacyWeapons.Sum(p => p.Price)
                });
            }

            return Ok(new EconomyStatsDto
            {
                TotalGoldCollected = totalGoldCollected,
                TotalGoldSpent = totalGoldSpent,
                GoldSpentOnWeapons = goldOnWeapons,
                GoldSpentOnPotions = goldOnPotions,
                SpendRatioPercent = spendRatio,
                AvgGoldPerSession = avgGoldPerSession,
                WeaponPurchaseCount = weaponPurchasesList.Count,
                PotionPurchaseCount = potionPurchasesList.Count,
                WeaponBreakdown = weaponBreakdown
            });
        }

        // Tổng hợp chỉ số chiến đấu trực tiếp từ GameSessions
        [HttpGet("combat")]
        public async Task<ActionResult<CombatStatsDto>> GetCombatStats(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? level = null,
            [FromQuery] int? tzOffset = null)
        {
            var sessions = await FilterSessions(from, to, level, tzOffset)
                .Select(s => new
                {
                    s.EnemiesKilled,
                    s.BossKilled,
                    s.BlockHits,
                    s.BlockedDamage,
                    s.PerfectParries,
                    s.TrapHits,
                    s.TrapDamage
                })
                .ToListAsync();

            int totalSessions = sessions.Count;
            int bossKills = sessions.Count(s => s.BossKilled);
            int blockHits = sessions.Sum(s => s.BlockHits);
            int parries = sessions.Sum(s => s.PerfectParries);
            int totalDefenses = blockHits + parries;

            return Ok(new CombatStatsDto
            {
                TotalEnemyKills = sessions.Sum(s => s.EnemiesKilled),
                BossKills = bossKills,
                TotalSessions = totalSessions,
                BossClearRatePercent = totalSessions > 0 ? Math.Round((double)bossKills / totalSessions * 100, 1) : 0,
                BlockHits = blockHits,
                TotalBlockedDamage = sessions.Sum(s => s.BlockedDamage),
                PerfectParries = parries,
                ParryMasteryPercent = totalDefenses > 0 ? Math.Round((double)parries / totalDefenses * 100, 1) : 0,
                TrapHits = sessions.Sum(s => s.TrapHits),
                TotalTrapDamage = sessions.Sum(s => s.TrapDamage)
            });
        }

        // Truy xuất dòng thời gian kết hợp cột mốc (GameEvents) và giao dịch mua đồ (ShopPurchases) của 1 phiên chơi
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
                .ToListAsync();

            var purchases = await _context.ShopPurchases
                .AsNoTracking()
                .Where(p => p.SessionId == sessionId)
                .ToListAsync();

            var startTime = session.StartTime;

            var timeline = events.Select(e => new SessionEventTimelineDto
                {
                    EventId = e.EventId,
                    EventType = e.EventType,
                    EventTime = e.EventTime,
                    Value = e.Value,
                    OffsetSeconds = Math.Max(0, Math.Round((e.EventTime - startTime).TotalSeconds, 1))
                })
                .Concat(purchases.Select(p => new SessionEventTimelineDto
                {
                    EventId = p.PurchaseId,
                    EventType = string.Equals(p.ItemType, "Potion", StringComparison.OrdinalIgnoreCase)
                        ? "potion_purchased"
                        : "weapon_purchased",
                    EventTime = p.PurchasedAt,
                    Value = p.Price,
                    OffsetSeconds = Math.Max(0, Math.Round((p.PurchasedAt - startTime).TotalSeconds, 1))
                }))
                .OrderBy(t => t.EventTime)
                .ThenBy(t => t.EventId)
                .ToList();

            return Ok(timeline);
        }

        // Kiểm tra kết quả phiên chơi thuộc nhóm thắng
        private static bool IsWinResult(string? result) => CharacterCatalog.IsWinResult(result);

        // Kiểm tra kết quả phiên chơi thuộc nhóm thua
        private static bool IsLoseResult(string? result) => CharacterCatalog.IsLoseResult(result);
    }
}
