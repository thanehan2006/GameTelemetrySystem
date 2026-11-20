using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    public class RegisterPlayerDto
    {
        [Required(ErrorMessage = "Tên người chơi không được để trống")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên người chơi phải từ 3 đến 50 ký tự")]
        public string PlayerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 đến 100 ký tự")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginPlayerDto
    {
        [Required(ErrorMessage = "Tên người chơi không được để trống")]
        [MaxLength(50, ErrorMessage = "Tên người chơi tối đa 50 ký tự")]
        public string PlayerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống")]
        [MaxLength(100, ErrorMessage = "Mật khẩu tối đa 100 ký tự")]
        public string Password { get; set; } = string.Empty;
    }

    public class PlayerResponseDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalSessions { get; set; }
    }
}
