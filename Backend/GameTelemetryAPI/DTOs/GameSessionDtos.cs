using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    // DTO yêu cầu bắt đầu phiên chơi mới cho người chơi
    public class StartSessionDto
    {
        [Required(ErrorMessage = "PlayerId là bắt buộc")]
        public int PlayerId { get; set; }
    }

    // DTO gửi kết quả kết thúc phiên chơi (Win, Lose hoặc Quit)
    public class EndSessionDto
    {
        [MaxLength(20, ErrorMessage = "Result tối đa 20 ký tự (ví dụ: win, lose, quit)")]
        public string? Result { get; set; }
    }

    // DTO trả về thông tin chi tiết của phiên chơi
    public class GameSessionResponseDto
    {
        public long SessionId { get; set; }
        public int PlayerId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Result { get; set; }
    }
}
