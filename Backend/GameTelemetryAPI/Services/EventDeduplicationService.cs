using System.Collections.Concurrent;

namespace GameTelemetryAPI.Services
{
    /// <summary>
    /// Chống ghi trùng sự kiện theo ClientEventId ở bộ nhớ ứng dụng (đặc biệt cho các sự kiện cộng dồn như enemy_killed, gold_collected).
    /// </summary>
    public class EventDeduplicationService
    {
        private static readonly TimeSpan EntryTtl = TimeSpan.FromHours(24);
        private const int CleanupThreshold = 20000;

        private readonly ConcurrentDictionary<string, DateTime> _seenIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Thử giữ chỗ một ClientEventId. Trả về false nếu ClientEventId đã được xử lý trong khoảng TTL.
        /// </summary>
        public bool TryReserve(string? clientEventId)
        {
            if (string.IsNullOrWhiteSpace(clientEventId))
            {
                return true;
            }

            string key = clientEventId.Trim();
            DateTime now = DateTime.UtcNow;

            if (_seenIds.Count > CleanupThreshold)
            {
                PruneExpired(now);
            }

            while (true)
            {
                if (_seenIds.TryAdd(key, now))
                {
                    return true;
                }

                if (!_seenIds.TryGetValue(key, out DateTime existingTime))
                {
                    continue;
                }

                if (now - existingTime < EntryTtl)
                {
                    return false;
                }

                if (_seenIds.TryUpdate(key, now, existingTime))
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Giải phóng khóa ClientEventId nếu giao dịch lưu CSDL thất bại do lỗi tạm thời, cho phép client gửi lại.
        /// </summary>
        public void Release(string? clientEventId)
        {
            if (string.IsNullOrWhiteSpace(clientEventId))
            {
                return;
            }

            _seenIds.TryRemove(clientEventId.Trim(), out _);
        }

        private void PruneExpired(DateTime now)
        {
            foreach (var kv in _seenIds)
            {
                if (now - kv.Value >= EntryTtl)
                {
                    _seenIds.TryRemove(kv.Key, out _);
                }
            }
        }
    }
}
