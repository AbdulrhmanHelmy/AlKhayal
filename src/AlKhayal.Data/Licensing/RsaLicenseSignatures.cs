using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using AlKhayal.Application.Licensing;

namespace AlKhayal.Data.Licensing
{
    public sealed class RsaLicenseSignatures : ILicenseSignatures
    {
        private readonly IDictionary<string, string> keys;
        public RsaLicenseSignatures(IDictionary<string, string> trustedPublicKeys) { keys = new Dictionary<string, string>(trustedPublicKeys); }
        public static RSACryptoServiceProvider CreateRsa()
        {
            var rsa = new RSACryptoServiceProvider(3072, new CspParameters(24) { Flags = CspProviderFlags.CreateEphemeralKey });
            rsa.PersistKeyInCsp = false;
            return rsa;
        }
        public bool Verify(string keyId, byte[] payload, byte[] signature)
        {
            string xml;
            if (!keys.TryGetValue(keyId, out xml)) return false;
            try
            {
                using (var rsa = CreateRsa())
                {
                    rsa.FromXmlString(xml);
                    if (!rsa.PublicOnly || rsa.KeySize != 3072) return false;
                    return rsa.VerifyData(payload, "SHA256", signature);
                }
            }
            catch (CryptographicException) { return false; }
        }
        public static RsaLicenseSignatures Embedded()
        {
            var keys = new Dictionary<string, string>();
            var assembly = typeof(RsaLicenseSignatures).Assembly;
            const string prefix = "AlKhayal.Data.Licensing.TrustedKeys.";
            foreach (var name in assembly.GetManifestResourceNames())
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal))
                    using (var reader = new StreamReader(assembly.GetManifestResourceStream(name)))
                        keys.Add(name.Substring(prefix.Length, name.Length - prefix.Length - 4), reader.ReadToEnd());
            return new RsaLicenseSignatures(keys);
        }
    }
}
