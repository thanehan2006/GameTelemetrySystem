using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SessionsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public SessionsController(GameTelemetryContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Bắt đầu một phiên chơi mới (gửi từ game khi khởi động/bắt đầu chơi)
        /// </summary>
        [HttpPost("start")]
        public async Task<ActionResult<GameSessionResponseDto>> StartSession([FromBody] StartSessionDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Kiểm tra Player có tồn tại không
            var playerExists = await _context.Players.AnyAsync(p => p.PlayerId == dto.PlayerId);
            if (!playerExists)
            {
                return NotFound(new { message = $"Không tìm thấy người chơi với PlayerId {dto.PlayerId}" });
            }

            var session = new GameSession
            {
                PlayerId = dto.PlayerId,
                StartTime = DateTime.UtcNow,
                EndTime = null,
                Result = null
            };

            _context.GameSessions.Add(session);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSessionById), new { id = session.SessionId }, new GameSessionResponseDto
            {
                SessionId = session.SessionId,
                PlayerId = session.PlayerId,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                Result = session.Result
            });
        }

        /// <summary>
        /// Kết thúc một phiên chơi (cập nhật EndTime và Result)
        /// </summary>
        [HttpPut("{id:long}/end")]
        public async Task<ActionResult<GameSessionResponseDto>> EndSession(long id, [FromBody] EndSessionDto dto)
        {
            var session = await _context.GameSessions.FindAsync(id);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {id}" });
            }

            session.EndTime = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dto.Result))
            {
                session.Result = dto.Result.Trim();
            }

            await _context.SaveChangesAsync();

            return Ok(new GameSessionResponseDto
            {
                SessionId = session.SessionId,
                PlayerId = session.PlayerId,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                Result = session.Result
            });
        }

        /// <summary>
        /// Lấy chi tiết phiên chơi theo ID
        /// </summary>
        [HttpGet("{id:long}")]
        public async Task<ActionResult<GameSessionResponseDto>> GetSessionById(long id)
        {
            var session = await _context.GameSessions.FindAsync(id);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {id}" });
            }

            return Ok(new GameSessionResponseDto
            {
                SessionId = session.SessionId,
                PlayerId = session.PlayerId,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                Result = session.Result
            });
        }
    }
}
