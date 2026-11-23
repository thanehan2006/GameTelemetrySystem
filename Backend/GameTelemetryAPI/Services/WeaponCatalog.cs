namespace GameTelemetryAPI.Services
{
    // Danh mục 8 vũ khí chuẩn đồng bộ giữa WeaponUpgradeConfig Unity, EventsController và AnalyticsController
    public static class WeaponCatalog
    {
        public readonly record struct WeaponItem(int Price, string Name, string Rarity, int BonusDamage);

        public static readonly WeaponItem[] Items =
        {
            new(75,  "Ngân Phong Kiếm",       "Hiếm",        8),
            new(130, "Thiên Lam Kiếm",        "Hiếm",        14),
            new(200, "Nham Thạch Chiến Chùy", "Sử Thi",      21),
            new(280, "Huyết Diễm Đoản Đao",   "Sử Thi",      28),
            new(370, "Hàn Ngọc Thánh Kiếm",   "Huyền Thoại", 36),
            new(470, "Lôi Quang Kim Thương",  "Huyền Thoại", 44),
            new(590, "Lưỡi Hái Huyết Nguyệt", "Thần Thoại",  54),
            new(720, "Phượng Hỏa Thần Kiếm",  "Thần Thoại",  65)
        };

        // Ánh xạ giá vàng mua vũ khí sang tên và bậc phẩm chất tương ứng
        public static (string Name, string Rarity) ResolveByPrice(int price)
        {
            foreach (var item in Items)
            {
                if (item.Price == price)
                {
                    return (item.Name, item.Rarity);
                }
            }

            return ("Vũ khí khác (bản cũ)", "Cũ");
        }
    }
}
