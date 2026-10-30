using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AlKhayal.Application.Licensing
{
    public sealed class SignedLicense
    {
        public string Payload { get; internal set; }
        public string SigningKeyId { get; internal set; }
        public IDictionary<string, string> Claims { get; internal set; }
        public byte[] Signature { get; internal set; }
    }

    // A fixed-order ASCII grammar: no escaping, optional fields, JSON, or duplicate keys.
    public static class LicenseFormat
    {
        public const int MaximumLength = 8192;
        private static readonly string[] Names = { "uuid", "board", "bios" };
        public static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Bytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static string Normalize(string raw)
        {
            var value = Regex.Replace((raw ?? "").Trim().ToUpperInvariant(), @"\s+", " ");
            var compact = Regex.Replace(value, "[^A-Z0-9]", "");
            if (compact.Length < 4 || compact.Distinct().Count() == 1 ||
                new[] { "UNKNOWN", "NONE", "DEFAULT", "DEFAULTSTRING", "TOBEFILLEDBYOEM", "SYSTEMSERIALNUMBER", "BASEBOARDSERIALNUMBER", "NOTAPPLICABLE", "NOTSPECIFIED", "NOTAVAILABLE", "NOASSETTAG", "OEM", "123456789", "1234567890", "0123456789" }.Contains(compact)) return null;
            return value;
        }
        public static IDictionary<string, string> HashClaims(IDictionary<string, string> raw)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in Names)
            {
                string value;
                if (raw.TryGetValue(name, out value) && (value = Normalize(value)) != null)
                    result[name] = Hash("AlKhayalPOS|binding-1|" + name + "|" + value);
            }
            return result;
        }
        private static string ClaimsText(IDictionary<string, string> claims)
        {
            if (claims == null || claims.Count < 2 || claims.Keys.Any(k => !Names.Contains(k)) || claims.Values.Any(v => !Regex.IsMatch(v ?? "", "^[a-f0-9]{64}$")))
                throw new FormatException("لا تتوفر هوية جهاز موثوقة كافية. يلزم معرفان صالحان على الأقل؛ تواصل مع الدعم.");
            return string.Join(";", Names.Where(claims.ContainsKey).Select(n => n + ":" + claims[n]));
        }
        private static IDictionary<string, string> ParseClaims(string value)
        {
            var result = new Dictionary<string, string>();
            foreach (var item in value.Split(';'))
            {
                var pair = item.Split(':');
                if (pair.Length != 2 || result.ContainsKey(pair[0])) throw Invalid();
                result.Add(pair[0], pair[1]);
            }
            if (ClaimsText(result) != value) throw Invalid();
            return result;
        }
        public static bool Matches(IDictionary<string, string> licensed, IDictionary<string, string> current)
        {
            if (licensed == null || current == null || licensed.Count < 2 || current.Count < 2) return false;
            return licensed.Count(p => current.ContainsKey(p.Key) && current[p.Key] == p.Value) >= 2;
        }
        public static string CreateRequest(IDictionary<string, string> claims)
        {
            var body = "ALKREQ|1|AlKhayalPOS|1|" + ClaimsText(claims);
            return body + "|" + Hash(body);
        }
        public static IDictionary<string, string> ParseRequest(string text)
        {
            var p = Split(text, 6);
            if (p[0] != "ALKREQ" || p[1] != "1" || p[2] != "AlKhayalPOS" || p[3] != "1" || p[5] != Hash(string.Join("|", p.Take(5)))) throw Invalid();
            return ParseClaims(p[4]);
        }
        public static string CreatePayload(IDictionary<string, string> claims, string keyId, Guid id, DateTime issuedUtc)
        {
            if (!Regex.IsMatch(keyId ?? "", "^[a-zA-Z0-9_-]{1,64}$") || id == Guid.Empty || issuedUtc.Kind != DateTimeKind.Utc) throw Invalid();
            return "ALKLIC|1|AlKhayalPOS|" + id.ToString("D") + "|Perpetual|1|" + ClaimsText(claims) + "|" + issuedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "|" + keyId + "|RSA3072-SHA256-PKCS1";
        }
        public static SignedLicense ParseLicense(string text)
        {
            var p = Split(text, 11);
            if (p[0] != "ALKLIC" || p[1] != "1") throw new FormatException("صيغة الترخيص أو إصدارها غير مدعوم.");
            if (p[2] != "AlKhayalPOS") throw new FormatException("الترخيص لمنتج آخر.");
            Guid id; DateTime issued;
            if (!Guid.TryParseExact(p[3], "D", out id) || id == Guid.Empty || p[4] != "Perpetual" || p[5] != "1" || p[9] != "RSA3072-SHA256-PKCS1" ||
                !DateTime.TryParseExact(p[7], "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out issued)) throw Invalid();
            var claims = ParseClaims(p[6]);
            var payload = CreatePayload(claims, p[8], id, issued);
            if (payload != string.Join("|", p.Take(10))) throw Invalid();
            byte[] signature;
            try { signature = Convert.FromBase64String(p[10]); } catch (FormatException) { throw Invalid(); }
            if (signature.Length != 384 || Convert.ToBase64String(signature) != p[10]) throw Invalid();
            return new SignedLicense { Payload = payload, Claims = claims, SigningKeyId = p[8], Signature = signature };
        }
        private static string[] Split(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaximumLength || text.Any(c => c < 33 || c > 126)) throw Invalid();
            var parts = text.Split('|');
            if (parts.Length != length) throw Invalid();
            return parts;
        }
        private static FormatException Invalid() => new FormatException("ملف التفعيل غير صالح أو تالف أو يحتوي حقولًا غير مدعومة.");
    }
}
