using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    // DTO nhận thông tin đăng ký tài khoản người chơi mới
    public class RegisterPlayerDto
    {
        [Required(ErrorMessage = "Tên người chơi không được để trống")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên người chơi phải từ 3 đến 50 ký tự")]
        public string PlayerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 đến 100 ký tự")]
        public string Password { get; set; } = string.Empty;
    }

    // DTO nhận thông tin đăng nhập từ game client
    public class LoginPlayerDto
    {
        [Required(ErrorMessage = "Tên người chơi không được để trống")]
        [MaxLength(50, ErrorMessage = "Tên người chơi tối đa 50 ký tự")]
        public string PlayerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống")]
        [MaxLength(100, ErrorMessage = "Mật khẩu tối đa 100 ký tự")]
        public string Password { get; set; } = string.Empty;
    }

    // DTO trả về thông tin người chơi sau khi xác thực hoặc truy vấn
    public class PlayerResponseDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalSessions { get; set; }
    }

    // DTO thống kê thành tích theo từng lớp nhân vật trong Hồ Sơ Chiến Binh
    public class PlayerCharacterStatDto
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string RoleBadge { get; set; } = string.Empty;
        public string AccentHex { get; set; } = "#f2bd47";
        public int Matches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double WinRatePercent { get; set; }
        public int TotalEnemiesKilled { get; set; }
        public double AvgEnemiesPerMatch { get; set; }
        public int BossKills { get; set; }
        public double TotalPlayTimeMinutes { get; set; }
    }

    // DTO tóm tắt một trận đấu gần nhất của người chơi
    public class PlayerRecentMatchDto
    {
        public long SessionId { get; set; }
        public int LevelId { get; set; }
        public string LevelName { get; set; } = string.Empty;
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool HasEndTime { get; set; }
        public double DurationMinutes { get; set; }
        public int EnemiesKilled { get; set; }
        public bool BossKilled { get; set; }
        public int GoldEarned { get; set; }
        public int PerfectParries { get; set; }
    }

    // DTO trọn gói cho màn hình Hồ Sơ Chiến Binh trong game
    public class PlayerProfileDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int QuitOrInProgress { get; set; }
        public double WinRatePercent { get; set; }
        public double TotalPlayTimeMinutes { get; set; }
        public int TotalEnemiesKilled { get; set; }
        public int TotalBossKills { get; set; }
        public int TotalPerfectParries { get; set; }
        public int TotalBlockHits { get; set; }
        public int TotalGoldEarned { get; set; }
        public int TotalGoldSpent { get; set; }
        public string FavoriteCharacterId { get; set; } = string.Empty;
        public string FavoriteCharacterName { get; set; } = string.Empty;
        public int FavoriteCharacterMatches { get; set; }
        public string FavoriteWeaponName { get; set; } = string.Empty;
        public int FavoriteWeaponPurchases { get; set; }
        public List<PlayerCharacterStatDto> CharacterStats { get; set; } = new();
        public List<PlayerRecentMatchDto> RecentMatches { get; set; } = new();
    }
}
