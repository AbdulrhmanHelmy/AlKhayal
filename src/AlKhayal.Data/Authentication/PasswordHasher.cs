using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using AlKhayal.Application.Authentication;

namespace AlKhayal.Data.Authentication
{
    public sealed class PasswordHasher : IPasswordHasher
    {
        private const int Iterations = 120000; private const int SaltBytes = 16; private const int KeyBytes = 32;
        public string Hash(string password)
        {
            if (password == null) throw new ArgumentNullException("password");
            var salt = new byte[SaltBytes]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var key = Derive(password, salt, Iterations); return string.Format(CultureInfo.InvariantCulture, "pbkdf2-sha256${0}${1}${2}", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
        }
        public bool Verify(string password, string encodedHash)
        {
            try { var p = encodedHash.Split('$'); if (p.Length != 4 || p[0] != "pbkdf2-sha256") return false; var salt = Convert.FromBase64String(p[2]); var expected = Convert.FromBase64String(p[3]); var actual = Derive(password, salt, int.Parse(p[1], CultureInfo.InvariantCulture)); return FixedTimeEquals(actual, expected); }
            catch { return false; }
        }
        private static byte[] Derive(string password, byte[] salt, int iterations)
        { using (var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(password))) { var output = new byte[KeyBytes]; var block = new byte[salt.Length + 4]; Buffer.BlockCopy(salt, 0, block, 0, salt.Length); block[block.Length - 1] = 1; var u = hmac.ComputeHash(block); Buffer.BlockCopy(u, 0, output, 0, u.Length); for (var i = 1; i < iterations; i++) { u = hmac.ComputeHash(u); for (var j = 0; j < output.Length; j++) output[j] ^= u[j]; } return output; } }
        private static bool FixedTimeEquals(byte[] a, byte[] b) { if (a == null || b == null || a.Length != b.Length) return false; var diff = 0; for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0; }
    }
    public sealed class SecureTokenGenerator : ITokenGenerator { public string CreateToken() { var b = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b); return Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_'); } }
    public sealed class SystemClock : IClock { public DateTime UtcNow { get { return DateTime.UtcNow; } } }
}
