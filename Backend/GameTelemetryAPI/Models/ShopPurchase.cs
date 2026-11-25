using System.Text.Json.Serialization;

namespace GameTelemetryAPI.Models
{
    // Lưu lịch sử mua vũ khí và bình hồi máu tại cửa hàng Lò Rèn
    public class ShopPurchase
    {
        public long PurchaseId { get; set; }
        public long SessionId { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? Rarity { get; set; }
        public int Price { get; set; }
        public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
        public string? ClientEventId { get; set; }

        [JsonIgnore]
        public GameSession? GameSession { get; set; }
    }
}
