using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public AnalyticsController(GameTelemetryContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 1. Lấy các chỉ số tổng quan (Overview): Số người chơi, số phiên chơi, số sự kiện, thời lượng chơi trung bình
        /// </summary>
        [HttpGet("overview")]
        public async Task<ActionResult<OverviewStatsDto>> GetOverviewStats()
        {
            var totalPlayers = await _context.Players.CountAsync();
            var totalSessions = await _context.GameSessions.CountAsync();
            var totalEvents = await _context.GameEvents.CountAsync();

            // Tính thời lượng trung bình của các phiên đã kết thúc (có EndTime)
            var finishedSessions = await _context.GameSessions
                .Where(s => s.EndTime != null)
                .Select(s => new { s.StartTime, EndTime = s.EndTime!.Value })
                .ToListAsync();

            double avgDuration = 0;
            if (finishedSessions.Count > 0)
            {
                avgDuration = finishedSessions
                    .Average(s => (s.EndTime - s.StartTime).TotalMinutes);
            }

            return Ok(new OverviewStatsDto
            {
                TotalPlayers = totalPlayers,
                TotalSessions = totalSessions,
                TotalEvents = totalEvents,
                AverageSessionDurationMinutes = Math.Round(avgDuration, 2)
            });
        }

        /// <summary>
        /// 2. Thống kê theo Level: Số lần bắt đầu, số lần hoàn thành, số lần chết, tỷ lệ hoàn thành (%)
        /// </summary>
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
                            (e.EventType == "level_start" || e.EventType == "level_complete" || e.EventType == "player_dead"))
                .Select(e => new { Level = e.Value!.Value, e.EventType })
                .ToListAsync();

            var result = events
                .GroupBy(e => e.Level)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    int starts = g.Count(x => x.EventType == "level_start");
                    int completions = g.Count(x => x.EventType == "level_complete");
                    int deaths = g.Count(x => x.EventType == "player_dead");
                    double rate = starts > 0 ? Math.Round((double)completions / starts * 100, 2) : 0;

                    return new LevelStatsDto
                    {
                        Level = g.Key,
                        Starts = starts,
                        Completions = completions,
                        Deaths = deaths,
                        CompletionRatePercent = rate
                    };
                })
                .ToList();

            return Ok(result);
        }

        /// <summary>
        /// 3. Thống kê phân bố các loại sự kiện (Event Types)
        /// </summary>
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

        /// <summary>
        /// 4. Thống kê số lượng phiên chơi theo ngày (Sessions by date)
        /// </summary>
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

        /// <summary>
        /// 5. Danh sách các phiên chơi gần đây (kèm thông tin chi tiết: người chơi, thời lượng, kết quả, số event)
        /// </summary>
        [HttpGet("recent-sessions")]
        public async Task<ActionResult<List<RecentSessionDto>>> GetRecentSessions([FromQuery] int limit = 10)
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
    }
}
