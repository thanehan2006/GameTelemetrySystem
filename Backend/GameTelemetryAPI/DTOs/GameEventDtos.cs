using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    public class CreateGameEventDto
    {
        [Required(ErrorMessage = "SessionId là bắt buộc")]
        public long SessionId { get; set; }

        [Required(ErrorMessage = "EventType là bắt buộc (ví dụ: login, game_start, level_start, level_complete, player_dead, game_exit)")]
        [MaxLength(50, ErrorMessage = "EventType tối đa 50 ký tự")]
        public string EventType { get; set; } = string.Empty;

        public DateTime? EventTime { get; set; }

        public int? Value { get; set; }
    }

    public class CreateBatchGameEventsDto
    {
        [Required(ErrorMessage = "SessionId là bắt buộc")]
        public long SessionId { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách sự kiện không được rỗng")]
        public List<SingleEventItemDto> Events { get; set; } = new List<SingleEventItemDto>();
    }

    public class SingleEventItemDto
    {
        [Required(ErrorMessage = "EventType là bắt buộc")]
        [MaxLength(50)]
        public string EventType { get; set; } = string.Empty;

        public DateTime? EventTime { get; set; }

        public int? Value { get; set; }
    }

    public class GameEventResponseDto
    {
        public long EventId { get; set; }
        public long SessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime EventTime { get; set; }
        public int? Value { get; set; }
    }
}
