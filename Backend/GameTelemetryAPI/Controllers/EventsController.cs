using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Controller tiếp nhận và truy vấn các sự kiện gameplay từ Unity client
    public class EventsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public EventsController(GameTelemetryContext context)
        {
            _context = context;
        }

        // Ghi nhận một sự kiện đơn lẻ vào phiên chơi tương ứng
        [HttpPost]
        public async Task<ActionResult<GameEventResponseDto>> RecordEvent([FromBody] CreateGameEventDto dto)
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

        // Ghi nhận đồng loạt nhiều sự kiện trong một lần gọi API
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

        // Lấy thông tin chi tiết của một sự kiện theo EventId
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
