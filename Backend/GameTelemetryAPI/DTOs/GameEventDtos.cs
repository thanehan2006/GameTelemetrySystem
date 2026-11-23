using System.ComponentModel.DataAnnotations;

namespace GameTelemetryAPI.DTOs
{
    // DTO nhận dữ liệu tạo một sự kiện telemetry đơn lẻ từ client
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

    // DTO nhận danh sách nhiều sự kiện gửi theo lô cho cùng một phiên chơi
    public class CreateBatchGameEventsDto
    {
        [Required(ErrorMessage = "SessionId là bắt buộc")]
        public long SessionId { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách sự kiện không được rỗng")]
        public List<SingleEventItemDto> Events { get; set; } = new List<SingleEventItemDto>();
    }

    // DTO mô tả từng phần tử sự kiện trong gói gửi hàng loạt
    public class SingleEventItemDto
    {
        [Required(ErrorMessage = "EventType là bắt buộc")]
        [MaxLength(50)]
        public string EventType { get; set; } = string.Empty;

        public DateTime? EventTime { get; set; }

        public int? Value { get; set; }
    }

    // DTO trả về thông tin sự kiện đã lưu
    public class GameEventResponseDto
    {
        public long EventId { get; set; }
        public long SessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime EventTime { get; set; }
        public int? Value { get; set; }
    }
}
