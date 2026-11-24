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
    // Controller tiếp nhận sự kiện từ Unity, phân tách cột mốc (GameEvents), giao dịch (ShopPurchases) và tổng hợp (GameSessions)
    public class EventsController : ControllerBase
    {
        private static readonly HashSet<string> CoreMilestoneEvents = new(StringComparer.OrdinalIgnoreCase)
        {
            "login",
            "game_start",
            "level_start",
            "level_complete",
            "level_failed",
            "player_dead",
            "boss_killed",
            "game_exit"
        };

        private readonly GameTelemetryContext _context;

        public EventsController(GameTelemetryContext context)
        {
            _context = context;
        }

        // Ghi nhận một sự kiện đơn lẻ và điều phối vào đúng bảng mục tiêu
        [HttpPost]
        public async Task<ActionResult<GameEventResponseDto>> RecordEvent([FromBody] CreateGameEventDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var session = await _context.GameSessions.FirstOrDefaultAsync(s => s.SessionId == dto.SessionId);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {dto.SessionId}" });
            }

            string eventType = dto.EventType.Trim();
            DateTime eventTime = NormalizeUtc(dto.EventTime);

            var createdEvent = ApplyEventToSession(session, eventType, eventTime, dto.Value);
            await _context.SaveChangesAsync();

            var responseDto = new GameEventResponseDto
            {
                EventId = createdEvent?.EventId ?? 0,
                SessionId = session.SessionId,
                EventType = eventType,
                EventTime = eventTime,
                Value = dto.Value
            };

            if (createdEvent != null)
            {
                return CreatedAtAction(nameof(GetEventById), new { id = createdEvent.EventId }, responseDto);
            }

            return Ok(responseDto);
        }

        // Ghi nhận đồng loạt nhiều sự kiện trong một lần gọi API từ Unity batch
        [HttpPost("batch")]
        public async Task<IActionResult> RecordEventsBatch([FromBody] CreateBatchGameEventsDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var session = await _context.GameSessions.FirstOrDefaultAsync(s => s.SessionId == dto.SessionId);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {dto.SessionId}" });
            }

            int processedCount = 0;
            foreach (var item in dto.Events)
            {
                if (string.IsNullOrWhiteSpace(item.EventType)) continue;
                ApplyEventToSession(session, item.EventType.Trim(), NormalizeUtc(item.EventTime), item.Value);
                processedCount++;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = $"Đã ghi nhận thành công {processedCount} sự kiện", count = processedCount });
        }

        // Lấy thông tin chi tiết của một sự kiện cột mốc theo EventId
        [HttpGet("{id:long}")]
        public async Task<ActionResult<GameEventResponseDto>> GetEventById(long id)
        {
            var gameEvent = await _context.GameEvents.FindAsync(id);
            if (gameEvent == null)
            {
                return NotFound(new { message = $"Không tìm thấy sự kiện với EventId {id}" });
            }

            return Ok(new GameEventResponseDto
            {
                EventId = gameEvent.EventId,
                SessionId = gameEvent.SessionId,
                EventType = gameEvent.EventType,
                EventTime = gameEvent.EventTime,
                Value = gameEvent.Value
            });
        }

        // Điều phối sự kiện: cột mốc vào GameEvents, mua đồ vào ShopPurchases, chỉ số chiến đấu/vàng cộng vào GameSessions
        private GameEvent? ApplyEventToSession(GameSession session, string eventType, DateTime eventTime, int? value)
        {
            string normalized = eventType.Trim().ToLowerInvariant();
            int safeValue = Math.Max(0, value ?? 0);

            if (CoreMilestoneEvents.Contains(normalized))
            {
                if ((normalized == "level_start" || session.LevelId == null)
                    && (normalized == "level_start" || normalized == "level_complete" || normalized == "level_failed" || normalized == "player_dead")
                    && value is >= 1 and <= 3)
                {
                    session.LevelId = value.Value;
                }

                if (normalized == "boss_killed")
                {
                    session.BossKilled = true;
                }

                var gameEvent = new GameEvent
                {
                    SessionId = session.SessionId,
                    EventType = normalized,
                    EventTime = eventTime,
                    Value = value
                };
                _context.GameEvents.Add(gameEvent);
                return gameEvent;
            }

            switch (normalized)
            {
                case "enemy_killed":
                    session.EnemiesKilled += 1;
                    break;

                case "gold_collected":
                    session.GoldEarned += safeValue;
                    break;

                case "block_hit":
                    session.BlockHits += 1;
                    session.BlockedDamage += safeValue;
                    break;

                case "perfect_parry":
                    session.PerfectParries += 1;
                    break;

                case "trap_hit":
                    session.TrapHits += 1;
                    session.TrapDamage += safeValue;
                    break;

                case "weapon_purchased":
                {
                    var (weaponName, rarity) = WeaponCatalog.ResolveByPrice(safeValue);
                    session.GoldSpent += safeValue;
                    _context.ShopPurchases.Add(new ShopPurchase
                    {
                        SessionId = session.SessionId,
                        ItemType = "Weapon",
                        ItemName = weaponName,
                        Rarity = rarity,
                        Price = safeValue,
                        PurchasedAt = eventTime
                    });
                    break;
                }

                case "potion_purchased":
                    session.GoldSpent += safeValue;
                    _context.ShopPurchases.Add(new ShopPurchase
                    {
                        SessionId = session.SessionId,
                        ItemType = "Potion",
                        ItemName = "Bình Hồi Máu Huyết Ngọc",
                        Rarity = "Tiêu Hao",
                        Price = safeValue,
                        PurchasedAt = eventTime
                    });
                    break;
            }

            return null;
        }

        private static DateTime NormalizeUtc(DateTime? value)
        {
            if (!value.HasValue) return DateTime.UtcNow;
            var dt = value.Value;
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
        }
    }
}
