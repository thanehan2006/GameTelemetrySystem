using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    public class RegisterPlayerDto
    {
        [Required(ErrorMessage = "Tên người chơi không được để trống")]
        [MaxLength(50, ErrorMessage = "Tên người chơi tối đa 50 ký tự")]
        public string PlayerName { get; set; } = string.Empty;
    }

    public class LoginPlayerDto
    {
        [Required(ErrorMessage = "Tên người chơi không được để trống")]
        [MaxLength(50, ErrorMessage = "Tên người chơi tối đa 50 ký tự")]
        public string PlayerName { get; set; } = string.Empty;
    }

    public class PlayerResponseDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalSessions { get; set; }
    }
}
