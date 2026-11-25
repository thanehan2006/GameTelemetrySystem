using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Controller quản lý vòng đời phiên chơi từ lúc bắt đầu đến khi kết thúc
    public class SessionsController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public SessionsController(GameTelemetryContext context)
        {
            _context = context;
        }

        // Tạo phiên chơi mới khi người chơi bắt đầu vào trận
        [HttpPost("start")]
        public async Task<ActionResult<GameSessionResponseDto>> StartSession([FromBody] StartSessionDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var playerExists = await _context.Players.AnyAsync(p => p.PlayerId == dto.PlayerId);
            if (!playerExists)
            {
                return NotFound(new { message = $"Không tìm thấy người chơi với PlayerId {dto.PlayerId}" });
            }

            var session = new GameSession
            {
                PlayerId = dto.PlayerId,
                LevelId = dto.LevelId is >= 1 and <= 3 ? dto.LevelId.Value : null,
                StartTime = DateTime.UtcNow,
                EndTime = null,
                Result = null
            };

            _context.GameSessions.Add(session);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSessionById), new { id = session.SessionId }, ToDto(session));
        }

        // Cập nhật thời gian kết thúc và kết quả cho phiên chơi
        [HttpPut("{id:long}/end")]
        public async Task<ActionResult<GameSessionResponseDto>> EndSession(long id, [FromBody] EndSessionDto dto)
        {
            var session = await _context.GameSessions.FindAsync(id);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {id}" });
            }

            string? incomingResult = string.IsNullOrWhiteSpace(dto.Result) ? null : dto.Result.Trim();

            if (session.EndTime != null)
            {
                if (IsFinalCombatResult(session.Result) && IsQuitOrEmpty(incomingResult))
                {
                    return Ok(ToDto(session));
                }

                if (incomingResult == null || string.Equals(session.Result, incomingResult, StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(ToDto(session));
                }
            }

            DateTime resolvedEnd = dto.EndTime.HasValue ? NormalizeUtc(dto.EndTime.Value) : DateTime.UtcNow;
            DateTime maxAllowed = DateTime.UtcNow.AddMinutes(1);
            if (resolvedEnd < session.StartTime)
            {
                resolvedEnd = session.StartTime;
            }
            else if (resolvedEnd > maxAllowed)
            {
                resolvedEnd = maxAllowed;
            }

            session.EndTime = resolvedEnd;
            if (incomingResult != null)
            {
                session.Result = incomingResult;
            }

            await _context.SaveChangesAsync();

            return Ok(ToDto(session));
        }

        // Truy vấn thông tin một phiên chơi theo SessionId
        [HttpGet("{id:long}")]
        public async Task<ActionResult<GameSessionResponseDto>> GetSessionById(long id)
        {
            var session = await _context.GameSessions.FindAsync(id);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {id}" });
            }

            return Ok(ToDto(session));
        }

        private static GameSessionResponseDto ToDto(GameSession session) => new GameSessionResponseDto
        {
            SessionId = session.SessionId,
            PlayerId = session.PlayerId,
            LevelId = session.LevelId,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            Result = session.Result
        };

        private static bool IsFinalCombatResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return false;
            var r = result.Trim().ToLowerInvariant();
            return r == "win" || r == "victory" || r == "completed" || r == "thắng"
                || r == "lose" || r == "loss" || r == "defeat" || r == "dead" || r == "thua";
        }

        private static bool IsQuitOrEmpty(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return true;
            var r = result.Trim().ToLowerInvariant();
            return r == "quit" || r == "exit" || r == "abandoned";
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
