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
    // Controller xử lý đăng ký, đăng nhập và truy vấn thông tin người chơi
    public class PlayersController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public PlayersController(GameTelemetryContext context)
        {
            _context = context;
        }

        // Tạo tài khoản người chơi mới và băm mật khẩu lưu vào cơ sở dữ liệu
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

        // Xác thực tên đăng nhập và mật khẩu của người chơi
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

        // Lấy thông tin chi tiết của một người chơi theo ID
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

        // Lấy trọn bộ Hồ Sơ Chiến Binh của người chơi: tổng quan, chiến tích theo tướng và lịch sử đấu
        [HttpGet("{id:int}/profile")]
        public async Task<ActionResult<PlayerProfileDto>> GetPlayerProfile(int id, [FromQuery] int recentLimit = 15)
        {
            var player = await _context.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PlayerId == id);

            if (player == null)
            {
                return NotFound(new { message = $"Không tìm thấy người chơi với ID {id}" });
            }

            var levelNames = await _context.Levels
                .AsNoTracking()
                .ToDictionaryAsync(l => l.LevelId, l => l.LevelName);

            var sessions = await _context.GameSessions
                .AsNoTracking()
                .Where(s => s.PlayerId == id)
                .OrderByDescending(s => s.StartTime)
                .ThenByDescending(s => s.SessionId)
                .Select(s => new
                {
                    s.SessionId,
                    s.LevelId,
                    s.CharacterId,
                    s.StartTime,
                    s.EndTime,
                    s.Result,
                    s.EnemiesKilled,
                    s.BossKilled,
                    s.GoldEarned,
                    s.GoldSpent,
                    s.BlockHits,
                    s.PerfectParries
                })
                .ToListAsync();

            var sessionIds = sessions.Select(s => s.SessionId).ToList();
            string favoriteWeaponName = "Chưa mua";
            int favoriteWeaponPurchases = 0;

            if (sessionIds.Count > 0)
            {
                var topWeapon = await _context.ShopPurchases
                    .AsNoTracking()
                    .Where(p => sessionIds.Contains(p.SessionId) && p.ItemType == "Weapon")
                    .GroupBy(p => p.ItemName)
                    .Select(g => new
                    {
                        ItemName = g.Key,
                        Count = g.Count(),
                        MaxPrice = g.Max(p => p.Price)
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.MaxPrice)
                    .FirstOrDefaultAsync();

                if (topWeapon != null)
                {
                    favoriteWeaponName = topWeapon.ItemName;
                    favoriteWeaponPurchases = topWeapon.Count;
                }
            }

            int totalMatches = sessions.Count;
            int wins = sessions.Count(s => CharacterCatalog.IsWinResult(s.Result));
            int losses = sessions.Count(s => CharacterCatalog.IsLoseResult(s.Result));
            int decided = wins + losses;
            double winRate = decided > 0 ? Math.Round((double)wins / decided * 100, 1) : 0;
            double totalPlayMinutes = Math.Round(
                sessions.Where(s => s.EndTime.HasValue)
                    .Sum(s => Math.Max(0, (s.EndTime!.Value - s.StartTime).TotalMinutes)),
                1);

            var charBuckets = CharacterCatalog.AggregateByCharacter(
                sessions.Select(s => new CharacterCatalog.CharacterSessionSample(
                    s.CharacterId,
                    s.Result,
                    s.EnemiesKilled,
                    s.BossKilled,
                    s.StartTime,
                    s.EndTime)));

            var characterStats = charBuckets.Select(b => new PlayerCharacterStatDto
            {
                CharacterId = b.Info.Id,
                CharacterName = b.Info.DisplayName,
                Title = b.Info.Title,
                RoleBadge = b.Info.RoleBadge,
                AccentHex = b.Info.AccentHex,
                Matches = b.Matches,
                Wins = b.Wins,
                Losses = b.Losses,
                WinRatePercent = b.WinRatePercent,
                TotalEnemiesKilled = b.TotalEnemiesKilled,
                AvgEnemiesPerMatch = b.AvgEnemiesPerMatch,
                BossKills = b.BossKills,
                TotalPlayTimeMinutes = Math.Round(b.TotalPlayMinutes, 1)
            }).ToList();

            var favChar = charBuckets.FirstOrDefault(b => b.Info.Id != "unknown" && b.Matches > 0);
            int safeRecentLimit = Math.Clamp(recentLimit, 1, 50);

            var recentMatches = sessions
                .Take(safeRecentLimit)
                .Select(s =>
                {
                    var charInfo = CharacterCatalog.Resolve(s.CharacterId);
                    string levelName = s.LevelId.HasValue && levelNames.TryGetValue(s.LevelId.Value, out var name)
                        ? name
                        : (s.LevelId.HasValue ? $"Level {s.LevelId.Value}" : "Chưa rõ");

                    return new PlayerRecentMatchDto
                    {
                        SessionId = s.SessionId,
                        LevelId = s.LevelId ?? 0,
                        LevelName = levelName,
                        CharacterId = charInfo.Id,
                        CharacterName = charInfo.DisplayName,
                        Result = string.IsNullOrWhiteSpace(s.Result) ? "In-Progress" : s.Result.Trim(),
                        StartTime = DateTime.SpecifyKind(s.StartTime, DateTimeKind.Utc),
                        EndTime = s.EndTime.HasValue ? DateTime.SpecifyKind(s.EndTime.Value, DateTimeKind.Utc) : null,
                        HasEndTime = s.EndTime.HasValue,
                        DurationMinutes = s.EndTime.HasValue
                            ? Math.Round(Math.Max(0, (s.EndTime.Value - s.StartTime).TotalMinutes), 1)
                            : 0,
                        EnemiesKilled = s.EnemiesKilled,
                        BossKilled = s.BossKilled,
                        GoldEarned = s.GoldEarned,
                        PerfectParries = s.PerfectParries
                    };
                })
                .ToList();

            return Ok(new PlayerProfileDto
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                CreatedAt = DateTime.SpecifyKind(player.CreatedAt, DateTimeKind.Utc),
                TotalMatches = totalMatches,
                Wins = wins,
                Losses = losses,
                QuitOrInProgress = Math.Max(0, totalMatches - decided),
                WinRatePercent = winRate,
                TotalPlayTimeMinutes = totalPlayMinutes,
                TotalEnemiesKilled = sessions.Sum(s => s.EnemiesKilled),
                TotalBossKills = sessions.Count(s => s.BossKilled),
                TotalPerfectParries = sessions.Sum(s => s.PerfectParries),
                TotalBlockHits = sessions.Sum(s => s.BlockHits),
                TotalGoldEarned = sessions.Sum(s => s.GoldEarned),
                TotalGoldSpent = sessions.Sum(s => s.GoldSpent),
                FavoriteCharacterId = favChar != null ? favChar.Info.Id : string.Empty,
                FavoriteCharacterName = favChar != null ? favChar.Info.DisplayName : "Chưa có",
                FavoriteCharacterMatches = favChar != null ? favChar.Matches : 0,
                FavoriteWeaponName = favoriteWeaponName,
                FavoriteWeaponPurchases = favoriteWeaponPurchases,
                CharacterStats = characterStats,
                RecentMatches = recentMatches
            });
        }

        // Lấy danh sách tất cả người chơi kèm tổng số phiên đã tham gia
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

        // Chuyển đổi thực thể Player sang đối tượng DTO trả về cho client
        private static PlayerResponseDto ToDto(Player player) => new PlayerResponseDto
        {
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            CreatedAt = player.CreatedAt
        };
    }
}
