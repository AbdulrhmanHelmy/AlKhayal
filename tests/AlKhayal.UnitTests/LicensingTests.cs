using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;

using Xunit;
namespace AlKhayal.UnitTests
{
    public sealed class LicensingTests : IDisposable
    {
        private readonly RSACryptoServiceProvider rsa = RsaLicenseSignatures.CreateRsa();
        private readonly string root = Path.Combine(Path.GetTempPath(), "alk-license-tests-" + Guid.NewGuid().ToString("N"));
        private readonly Hardware hardware = new Hardware();
        public LicensingTests() { Directory.CreateDirectory(root); }
        public void Dispose() { rsa.Dispose(); Directory.Delete(root, true); }
        private string Public => rsa.ToXmlString(false);
        private string Id => "rsa-" + LicenseFormat.Hash(Public).Substring(0,24);
        private LicenseService Service(IHardwareIdentity identity = null) => new LicenseService(new FileLicenseStorage(Path.Combine(root, "license.alklic")), new RsaLicenseSignatures(new Dictionary<string, string> { { Id, Public } }), identity ?? hardware);
        private string Sign(string payload) => payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload), "SHA256"));
        private string License() => Sign(LicenseFormat.CreatePayload(hardware.ReadClaims(), Id, Guid.NewGuid(), DateTime.UtcNow));
        [Fact] public void Rsa3072_perpetual_activation_restarts_reimports_and_replaces_atomically()
        {
            var text = License(); Assert.Equal(3072, rsa.KeySize); Assert.False(Service().Check().IsValid);
            Assert.True(Service().Activate(text).IsValid); Assert.True(Service().Check().IsValid);
            Assert.True(Service().Activate(text).IsValid); Assert.True(Service().Activate(License()).IsValid);
            Assert.Single(Directory.GetFiles(root)); Assert.DoesNotContain("Expires", text); Assert.DoesNotContain("Version=", text);
        }
        [Fact] public void Removed_moved_corrupt_oversized_and_non_utf8_files_lock_and_recover()
        {
            var service = Service(); var text = License(); var path = Path.Combine(root, "license.alklic");
            Assert.True(service.Activate(text).IsValid); File.Move(path, path + ".moved"); Assert.False(service.Check().IsValid);
            File.WriteAllText(path, "corrupt"); Assert.False(service.Check().IsValid);
            File.WriteAllText(path, new string('a', 9000)); Assert.False(service.Check().IsValid);
            File.WriteAllBytes(path, new byte[] { 0xff, 0xfe }); Assert.False(service.Check().IsValid);
            Assert.True(service.Activate(text).IsValid);
        }
        [Theory]
        [InlineData("product")][InlineData("version")][InlineData("type")][InlineData("algorithm")][InlineData("key")][InlineData("id")][InlineData("date")][InlineData("binding")][InlineData("claim")][InlineData("signature")][InlineData("duplicate")][InlineData("expiry")][InlineData("oversized")][InlineData("malformed")]
        public void Rejects_tampering_and_unsupported_fields(string mutation)
        {
            var text = License(); var p = text.Split('|');
            switch(mutation)
            {
                case "product": p[2] = "OtherPOS"; break; case "version": p[1] = "2"; break;
                case "type": p[4] = "Trial"; break; case "algorithm": p[9] = "RSA-PSS"; break;
                case "key": p[8] = "untrusted"; break; case "id": p[3] = Guid.NewGuid().ToString("D"); break;
                case "date": p[7] = "2020-01-01T00:00:00Z"; break; case "binding": p[5] = "2"; break;
                case "claim": p[6] = p[6].Replace("uuid:", "uuid:" + "a"); break;
                case "signature": p[10] = Convert.ToBase64String(new byte[384]); break;
                case "duplicate": p[6] += ";" + p[6].Split(';')[0]; break;
                case "expiry": text += "|ExpiresUtc=2099-01-01"; break;
                case "oversized": text = new string('x', 9000); break; case "malformed": text = "{}"; break;
            }
            if (mutation != "expiry" && mutation != "oversized" && mutation != "malformed") text = string.Join("|", p);
            Assert.False(Service().Validate(text).IsValid); Assert.False(Service().Activate(text).IsValid); Assert.False(Service().Check().IsValid);
        }
        [Fact] public void Wrong_public_key_and_PSS_padding_rejected()
        {
            using(var other = RsaLicenseSignatures.CreateRsa())
            {
                var parsed = LicenseFormat.ParseLicense(License());
                Assert.False(new RsaLicenseSignatures(new Dictionary<string,string>{{ Id, other.ToXmlString(false) }}).Verify(Id, LicenseFormat.Bytes(parsed.Payload), parsed.Signature));
                using(var cng = new RSACng())
                {
                    cng.ImportParameters(rsa.ExportParameters(true));
                    var sig = cng.SignData(LicenseFormat.Bytes(parsed.Payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    Assert.False(Service().Validate(parsed.Payload + "|" + Convert.ToBase64String(sig)).IsValid);
                }
            }
        }
        [Theory][InlineData("uuid")][InlineData("board")][InlineData("bios")]
        public void One_component_change_tolerated_when_three_were_issued(string changed)
        { var text = License(); var next = new Hardware(); next.Raw[changed] = "replacement-" + changed; Assert.True(Service(next).Validate(text).IsValid); }
        [Theory][InlineData("bios")][InlineData("board")]
        public void One_missing_identifier_can_issue_and_validate_but_needs_both_matches(string missing)
        {
            hardware.Raw.Remove(missing); var text = License(); Assert.True(Service().Validate(text).IsValid);
            var next = new Hardware(); next.Raw[missing == "bios" ? "board" : "bios"] = "different-component";
            Assert.False(Service(next).Validate(text).IsValid);
        }
        [Fact] public void Another_device_or_significant_change_rejected()
        {
            var text = License(); var next = new Hardware(); next.Raw["uuid"] = "another-device-uuid"; next.Raw["board"] = "another-board";
            Assert.False(Service(next).Validate(text).IsValid);
        }
        [Theory][InlineData("")][InlineData("Unknown")][InlineData("To be filled by O.E.M.")][InlineData("Default string")][InlineData("00000000-0000-0000-0000-000000000000")][InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")][InlineData("System Serial Number")]
        public void Generic_identifiers_rejected(string raw) { Assert.Null(LicenseFormat.Normalize(raw)); }
        [Fact] public void Normalization_is_invariant_and_no_information_cannot_activate()
        {
            Assert.Equal(LicenseFormat.Normalize(" abcd  efgh "), LicenseFormat.Normalize("ABCD EFGH"));
            var empty = new Hardware(); empty.Raw.Clear(); Assert.Throws<FormatException>(() => Service(empty).CreateRequest());
            Assert.False(Service(empty).Validate(License()).IsValid);
            empty.Raw["uuid"] = "only-one-good-identifier"; Assert.Throws<FormatException>(() => Service(empty).CreateRequest());
        }
        [Fact] public void Request_is_private_deterministic_checksummed_and_never_a_license()
        {
            var request = Service().CreateRequest(); Assert.Equal(request, Service().CreateRequest());
            Assert.Equal(3, LicenseFormat.ParseRequest(request).Count); Assert.DoesNotContain("device-uuid", request);
            Assert.False(Service().Validate(request).IsValid);
            Assert.Throws<FormatException>(() => LicenseFormat.ParseRequest(request + "a"));
            Assert.Throws<FormatException>(() => LicenseFormat.ParseRequest(request.Replace("AlKhayalPOS", "Other")));
        }
        [Fact] public void License_directory_ACL_gives_cashiers_read_only_and_administrators_full_control()
        {
            var directory = Path.Combine(root,"acl"); FileLicenseStorage.InitializeDirectory(directory);
            var acl = Directory.GetAccessControl(directory);
            Assert.True(acl.AreAccessRulesProtected);
            var rules = acl.GetAccessRules(true,false,typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToArray();
            var users = rules.Single(r => r.IdentityReference.Value == "S-1-5-32-545");
            Assert.Equal(System.Security.AccessControl.FileSystemRights.ReadAndExecute,users.FileSystemRights & ~System.Security.AccessControl.FileSystemRights.Synchronize);
            Assert.Contains(rules,r => r.IdentityReference.Value == "S-1-5-32-544" && r.FileSystemRights == System.Security.AccessControl.FileSystemRights.FullControl);
            var storage = new FileLicenseStorage(Path.Combine(directory,"license.alklic"));
            if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                // A non-elevated cashier must be unable to install a machine license.
                Assert.Throws<UnauthorizedAccessException>(() => storage.Write(License()));
                // Give only this disposable test directory's owner write rights to exercise atomic inheritance.
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User,
                    System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Allow));
                Directory.SetAccessControl(directory,acl);
            }
            storage.Write(License()); storage.Write(License());
            Assert.Single(Directory.GetFiles(directory));
            Assert.DoesNotContain(File.GetAccessControl(Path.Combine(directory,"license.alklic")).GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>(),
                r => r.IdentityReference.Value == "S-1-5-32-545" && (r.FileSystemRights & System.Security.AccessControl.FileSystemRights.WriteData) != 0);
        }
        [Fact] public void Embedded_keyring_has_no_test_or_owner_private_material()
        {
            var parsed = LicenseFormat.ParseLicense(License());
            Assert.False(RsaLicenseSignatures.Embedded().Verify(Id, LicenseFormat.Bytes(parsed.Payload), parsed.Signature));
            Assert.DoesNotContain(typeof(RsaLicenseSignatures).Assembly.GetManifestResourceNames(), x => x.EndsWith(".pfx"));
        }
        [Fact] public void Customer_build_guard_accepts_public_RSA3072_and_rejects_private_key_material()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName,"AlKhayal.slnx"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var script = Path.Combine(directory.FullName,"scripts","validate-license-public-keys.ps1");
            var keyPath = Path.Combine(root,"disposable-test-key.xml"); File.WriteAllText(keyPath,Public);
            Func<int> validate = () =>
            {
                var start = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -KeyDirectory \"" + root + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using(var process = System.Diagnostics.Process.Start(start))
                { process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); Assert.True(process.WaitForExit(15000)); return process.ExitCode; }
            };
            Assert.Equal(0,validate()); File.WriteAllText(keyPath,rsa.ToXmlString(true)); Assert.NotEqual(0,validate());
        }
        private sealed class Hardware : IHardwareIdentity
        {
            public Dictionary<string,string> Raw = new Dictionary<string,string> { {"uuid","device-uuid-12345"}, {"board","board-serial-6789"}, {"bios","bios-serial-abcdef"} };
            public IDictionary<string,string> ReadClaims() => LicenseFormat.HashClaims(Raw);
        }
    }
}
