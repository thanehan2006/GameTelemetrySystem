using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    public class StartSessionDto
    {
        [Required(ErrorMessage = "PlayerId là bắt buộc")]
        public int PlayerId { get; set; }
    }

    public class EndSessionDto
    {
        [MaxLength(20, ErrorMessage = "Result tối đa 20 ký tự (ví dụ: win, lose, quit)")]
        public string? Result { get; set; }
    }

    public class GameSessionResponseDto
    {
        public long SessionId { get; set; }
        public int PlayerId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Result { get; set; }
    }
}
