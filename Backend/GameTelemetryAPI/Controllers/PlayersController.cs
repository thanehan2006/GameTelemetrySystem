using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;

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
        /// Đăng ký người chơi mới (nếu tên đã tồn tại thì báo lỗi 409 Conflict)
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult<PlayerResponseDto>> Register([FromBody] RegisterPlayerDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var trimmedName = dto.PlayerName.Trim();
            var existingPlayer = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerName == trimmedName);

            if (existingPlayer != null)
            {
                return Conflict(new { message = $"Tên người chơi '{trimmedName}' đã tồn tại. Vui lòng chọn tên khác hoặc đăng nhập." });
            }

            var newPlayer = new Player
            {
                PlayerName = trimmedName,
                CreatedAt = DateTime.UtcNow
            };

            _context.Players.Add(newPlayer);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPlayerById), new { id = newPlayer.PlayerId }, new PlayerResponseDto
            {
                PlayerId = newPlayer.PlayerId,
                PlayerName = newPlayer.PlayerName,
                CreatedAt = newPlayer.CreatedAt
            });
        }

        /// <summary>
        /// Đăng nhập người chơi bằng PlayerName
        /// </summary>
        [HttpPost("login")]
        public async Task<ActionResult<PlayerResponseDto>> Login([FromBody] LoginPlayerDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var trimmedName = dto.PlayerName.Trim();
            var player = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerName == trimmedName);

            if (player == null)
            {
                return NotFound(new { message = $"Không tìm thấy người chơi có tên '{trimmedName}'. Vui lòng đăng ký trước." });
            }

            return Ok(new PlayerResponseDto
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                CreatedAt = player.CreatedAt
            });
        }

        /// <summary>
        /// Lấy thông tin người chơi theo ID
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PlayerResponseDto>> GetPlayerById(int id)
        {
            var player = await _context.Players.FindAsync(id);
            if (player == null)
            {
                return NotFound(new { message = $"Không tìm thấy người chơi với ID {id}" });
            }

            return Ok(new PlayerResponseDto
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                CreatedAt = player.CreatedAt
            });
        }

        /// <summary>
        /// Lấy danh sách tất cả người chơi
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<PlayerResponseDto>>> GetAllPlayers()
        {
            var players = await _context.Players
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PlayerResponseDto
                {
                    PlayerId = p.PlayerId,
                    PlayerName = p.PlayerName,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            return Ok(players);
        }
    }
}
