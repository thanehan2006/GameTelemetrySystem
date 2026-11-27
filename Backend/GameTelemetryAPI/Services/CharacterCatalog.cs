namespace GameTelemetryAPI.Services
{
    // Danh mục 4 lớp nhân vật chuẩn đồng bộ giữa CharacterRosterConfig Unity và hệ thống Telemetry
    public static class CharacterCatalog
    {
        public readonly record struct CharacterInfo(
            string Id,
            string DisplayName,
            string Title,
            string RoleBadge,
            string AccentHex);

        public static readonly CharacterInfo[] Items =
        {
            new("vanguard", "VANGUARD", "Chiến Binh Tiên Phong", "CÂN BẰNG",               "#f2bd47"),
            new("maria",    "MARIA",    "Kiếm Sĩ Bão Tố",        "TỐC ĐỘ CAO",             "#47d6fa"),
            new("vampire",  "VAMPIRE",  "Lãnh Chúa Huyết Nguyệt","SÁT THƯƠNG / HỒI PHỤC",  "#f2425c"),
            new("maw",      "MAW",      "Khổng Lồ Phá Giáp",     "TANKER / ĐẨY LÙI",       "#bd70fa")
        };

        private static readonly CharacterInfo UnknownCharacter =
            new("unknown", "Tướng (bản cũ)", "Phiên trước khi lưu tướng", "BẢN CŨ", "#94a3b8");

        // Chuẩn hóa CharacterId đầu vào (chữ thường, tối đa 50 ký tự)
        public static string? NormalizeId(string? rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return null;
            string norm = rawId.Trim().ToLowerInvariant();
            return norm.Length > 50 ? norm.Substring(0, 50) : norm;
        }

        // Ánh xạ CharacterId sang thông tin hiển thị đầy đủ
        public static CharacterInfo Resolve(string? rawId)
        {
            string? norm = NormalizeId(rawId);
            if (norm == null) return UnknownCharacter;

            foreach (var item in Items)
            {
                if (item.Id == norm)
                {
                    return item;
                }
            }

            return new CharacterInfo(norm, norm.ToUpperInvariant(), "Chiến Binh", "KHÁC", "#94a3b8");
        }

        internal readonly record struct CharacterSessionSample(
            string? CharacterId,
            string? Result,
            int EnemiesKilled,
            bool BossKilled,
            DateTime StartTime,
            DateTime? EndTime);

        internal sealed class CharacterBucket
        {
            public CharacterInfo Info { get; init; }
            public int Matches { get; set; }
            public int Wins { get; set; }
            public int Losses { get; set; }
            public int TotalEnemiesKilled { get; set; }
            public int BossKills { get; set; }
            public double TotalPlayMinutes { get; set; }
            public int FinishedMatches { get; set; }

            public double WinRatePercent => (Wins + Losses) > 0
                ? Math.Round((double)Wins / (Wins + Losses) * 100, 1)
                : 0;

            public double AvgEnemiesPerMatch => Matches > 0
                ? Math.Round((double)TotalEnemiesKilled / Matches, 1)
                : 0;

            public double AvgDurationMinutes => FinishedMatches > 0
                ? Math.Round(TotalPlayMinutes / FinishedMatches, 1)
                : 0;
        }

        internal static bool IsWinResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return false;
            string r = result.Trim().ToLowerInvariant();
            return r == "win" || r == "victory" || r == "completed" || r == "thắng";
        }

        internal static bool IsLoseResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return false;
            string r = result.Trim().ToLowerInvariant();
            return r == "lose" || r == "loss" || r == "defeat" || r == "dead" || r == "thua";
        }

        // Nhóm dữ liệu phiên chơi theo 4 lớp nhân vật chuẩn (và nhóm cũ nếu có) để dùng chung cho Profile & Analytics
        internal static List<CharacterBucket> AggregateByCharacter(IEnumerable<CharacterSessionSample> samples)
        {
            var buckets = new Dictionary<string, CharacterBucket>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in Items)
            {
                buckets[item.Id] = new CharacterBucket { Info = item };
            }

            foreach (var s in samples)
            {
                CharacterInfo info = Resolve(s.CharacterId);
                if (!buckets.TryGetValue(info.Id, out var bucket))
                {
                    bucket = new CharacterBucket { Info = info };
                    buckets[info.Id] = bucket;
                }

                bucket.Matches++;
                if (IsWinResult(s.Result)) bucket.Wins++;
                else if (IsLoseResult(s.Result)) bucket.Losses++;

                bucket.TotalEnemiesKilled += s.EnemiesKilled;
                if (s.BossKilled) bucket.BossKills++;

                if (s.EndTime.HasValue)
                {
                    bucket.TotalPlayMinutes += Math.Max(0, (s.EndTime.Value - s.StartTime).TotalMinutes);
                    bucket.FinishedMatches++;
                }
            }

            return buckets.Values
                .Where(b => b.Info.Id != UnknownCharacter.Id || b.Matches > 0)
                .OrderBy(b => b.Info.Id == UnknownCharacter.Id ? 1 : 0)
                .ThenByDescending(b => b.Matches)
                .ThenByDescending(b => b.Wins)
                .ToList();
        }
    }
}
