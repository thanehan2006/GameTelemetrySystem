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
    public class PlayersController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public PlayersController(GameTelemetryContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Đăng ký tài khoản người chơi mới kèm mật khẩu (mã hóa PBKDF2).
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult<PlayerResponseDto>> Register([FromBody] RegisterPlayerDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            string trimmedName = dto.PlayerName.Trim();
            if (trimmedName.Length < 3)
            {
                return BadRequest(new { message = "Tên người chơi phải có ít nhất 3 ký tự." });
            }

            bool exists = await _context.Players.AnyAsync(p => p.PlayerName == trimmedName);
            if (exists)
            {
                return Conflict(new { message = $"Tên người chơi '{trimmedName}' đã tồn tại. Vui lòng chọn tên khác hoặc đăng nhập." });
            }

            var newPlayer = new Player
            {
                PlayerName = trimmedName,
                PasswordHash = PasswordHasherHelper.Hash(dto.Password),
                CreatedAt = DateTime.UtcNow
            };

            _context.Players.Add(newPlayer);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPlayerById), new { id = newPlayer.PlayerId }, ToDto(newPlayer));
        }

        /// <summary>
        /// Đăng nhập người chơi bằng PlayerName + Password.
        /// </summary>
        [HttpPost("login")]
        public async Task<ActionResult<PlayerResponseDto>> Login([FromBody] LoginPlayerDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            string trimmedName = dto.PlayerName.Trim();
            var player = await _context.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PlayerName == trimmedName);

            if (player == null)
            {
                return Unauthorized(new { message = "Sai tên đăng nhập hoặc mật khẩu!" });
            }

            if (string.IsNullOrEmpty(player.PasswordHash))
            {
                return Unauthorized(new { message = "Tài khoản cũ chưa có mật khẩu. Vui lòng đăng ký tài khoản mới." });
            }

            if (!PasswordHasherHelper.Verify(dto.Password, player.PasswordHash))
            {
                return Unauthorized(new { message = "Sai tên đăng nhập hoặc mật khẩu!" });
            }

            return Ok(ToDto(player));
        }

        /// <summary>
        /// Lấy thông tin người chơi theo ID.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PlayerResponseDto>> GetPlayerById(int id)
        {
            var player = await _context.Players.AsNoTracking().FirstOrDefaultAsync(p => p.PlayerId == id);
            if (player == null)
            {
                return NotFound(new { message = $"Không tìm thấy người chơi với ID {id}" });
            }

            return Ok(ToDto(player));
        }

        /// <summary>
        /// Lấy danh sách tất cả người chơi.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<PlayerResponseDto>>> GetAllPlayers()
        {
            var players = await _context.Players
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PlayerResponseDto
                {
                    PlayerId = p.PlayerId,
                    PlayerName = p.PlayerName,
                    CreatedAt = p.CreatedAt,
                    TotalSessions = p.GameSessions.Count
                })
                .ToListAsync();

            return Ok(players);
        }

        private static PlayerResponseDto ToDto(Player player) => new PlayerResponseDto
        {
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            CreatedAt = player.CreatedAt
        };
    }
}
