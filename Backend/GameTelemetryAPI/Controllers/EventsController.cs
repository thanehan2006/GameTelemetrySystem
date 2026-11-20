using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public EventsController(GameTelemetryContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Ghi nhận 1 sự kiện telemetry từ game (login, game_start, level_start, level_complete, player_dead, game_exit, ...)
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<GameEventResponseDto>> RecordEvent([FromBody] CreateGameEventDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Kiểm tra Session có tồn tại không
            var sessionExists = await _context.GameSessions.AnyAsync(s => s.SessionId == dto.SessionId);
            if (!sessionExists)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {dto.SessionId}" });
            }

            var gameEvent = new GameEvent
            {
                SessionId = dto.SessionId,
                EventType = dto.EventType.Trim(),
                EventTime = dto.EventTime ?? DateTime.UtcNow,
                Value = dto.Value
            };

            _context.GameEvents.Add(gameEvent);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetEventById), new { id = gameEvent.EventId }, new GameEventResponseDto
            {
                EventId = gameEvent.EventId,
                SessionId = gameEvent.SessionId,
                EventType = gameEvent.EventType,
                EventTime = gameEvent.EventTime,
                Value = gameEvent.Value
            });
        }

        /// <summary>
        /// Gửi theo lô (Batch) nhiều sự kiện cùng lúc (phù hợp khi game gom event gửi 1 lần hoặc khi mạng chập chờn)
        /// </summary>
        [HttpPost("batch")]
        public async Task<IActionResult> RecordEventsBatch([FromBody] CreateBatchGameEventsDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var sessionExists = await _context.GameSessions.AnyAsync(s => s.SessionId == dto.SessionId);
            if (!sessionExists)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {dto.SessionId}" });
            }

            var events = dto.Events.Select(e => new GameEvent
            {
                SessionId = dto.SessionId,
                EventType = e.EventType.Trim(),
                EventTime = e.EventTime ?? DateTime.UtcNow,
                Value = e.Value
            }).ToList();

            _context.GameEvents.AddRange(events);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Đã ghi nhận thành công {events.Count} sự kiện", count = events.Count });
        }

        /// <summary>
        /// Lấy chi tiết một sự kiện theo ID
        /// </summary>
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
    }
}
