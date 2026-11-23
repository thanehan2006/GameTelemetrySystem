using System.Security.Cryptography;

namespace GameTelemetryAPI.Services
{

    // Lớp tiện ích băm mật khẩu kèm salt ngẫu nhiên bằng PBKDF2-SHA256
    public static class PasswordHasherHelper
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;
        private const string Prefix = "$pbkdf2";

        // Tạo chuỗi hash bảo mật từ mật khẩu gốc để lưu xuống cơ sở dữ liệu
        public static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        // Kiểm tra mật khẩu người dùng nhập có khớp với chuỗi hash đã lưu hay không
        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            string[] parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[0] != "pbkdf2")
                return false;

            if (!int.TryParse(parts[1], out int iterations) || iterations <= 0)
                return false;

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);
                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
