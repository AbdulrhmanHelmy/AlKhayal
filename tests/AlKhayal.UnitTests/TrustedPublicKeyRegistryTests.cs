using System;
using System.IO;
using System.Linq;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class TrustedPublicKeyRegistryTests
    {
        [Fact]
        public void Embedded_trusted_resources_are_public_RSA3072_and_named_by_their_canonical_key_id()
        {
            var assembly = typeof(RsaLicenseSignatures).Assembly;
            const string prefix = "AlKhayal.Data.Licensing.TrustedKeys.";
            var resources = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(resources);
            foreach (var resource in resources)
            {
                using (var reader = new StreamReader(assembly.GetManifestResourceStream(resource)))
                using (var rsa = RsaLicenseSignatures.CreateRsa())
                {
                    var xml = reader.ReadToEnd();
                    rsa.FromXmlString(xml);
                    Assert.True(rsa.PublicOnly);
                    Assert.Equal(3072, rsa.KeySize);
                    Assert.Equal(rsa.ToXmlString(false), xml);
                    Assert.Equal(prefix + "rsa-" + LicenseFormat.Hash(xml).Substring(0, 24) + ".xml", resource);
                }
            }
        }
    }
}
