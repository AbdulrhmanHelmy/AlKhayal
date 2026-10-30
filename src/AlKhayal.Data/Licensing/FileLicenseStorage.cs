using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Diagnostics;
using AlKhayal.Application.Licensing;

namespace AlKhayal.Data.Licensing
{
    public sealed class FileLicenseStorage : ILicenseStorage
    {
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AlKhayal", "Licensing", "license.alklic");
        private readonly string path;
        public FileLicenseStorage(string path) { this.path = Path.GetFullPath(path); }
        public string Read()
        {
            if (!File.Exists(path)) return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length > LicenseFormat.MaximumLength) throw new IOException("License too large.");
                try { using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false)) return reader.ReadToEnd(); }
                catch (DecoderFallbackException e) { throw new IOException("Invalid license encoding.", e); }
            }
        }
        public void Write(string text) => Write(text, null);
        internal void Write(string text, FileSecurity security)
        {
            LicenseFormat.ParseLicense(text);
            var directory = Path.GetDirectoryName(path);
            if (string.Equals(path, DefaultPath, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(directory))
                throw new IOException("Initialize machine license storage as administrator first.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            var stage = "create-temp";
            try
            {
                using (var file = security == null
                    ? new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    : new FileStream(temporary, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security))
                {
                    var bytes = LicenseFormat.Bytes(text);
                    stage = "write-and-flush";
                    file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                stage = File.Exists(path) ? "atomic-replace" : "initial-move";
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { Trace.TraceError("License persistence failed at {0}: {1} (0x{2:X8})", stage, error.GetType().Name, error.HResult); throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Owner/installer runs explicitly, elevated. Cashiers read; administrators replace.
        public static void InitializeMachineDirectory()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Administrator elevation is required to initialize machine licensing storage.");
            using (WindowsLicensePath.PrepareMachineDirectory()) { }
        }
        public static void InitializeDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.BuiltinUsersSid })
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), sid == WellKnownSidType.BuiltinUsersSid ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(directory, acl);
        }
    }
    public static class MachineLicensing
    {
        public static LicenseService Create() => new LicenseService(new FileLicenseStorage(FileLicenseStorage.DefaultPath), RsaLicenseSignatures.Embedded(), new WindowsHardwareIdentity());
    }
}
