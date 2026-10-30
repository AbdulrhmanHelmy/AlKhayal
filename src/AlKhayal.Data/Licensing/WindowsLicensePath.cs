using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using AlKhayal.Application.Licensing;

namespace AlKhayal.Data.Licensing
{
    // No-share-delete directory handles prevent renaming during installation. Application-owned
    // ancestors are hardened under a separate no-share-write handle before child creation.
    // OPEN_REPARSE_POINT inspects the object itself rather than following a junction/symlink.
    public sealed class WindowsLicensePath : IDisposable
    {
        private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
        private WindowsLicensePath() { }
        public static WindowsLicensePath InspectDirectory(string directory)
        { return Acquire(directory, false); }
        internal static WindowsLicensePath PrepareMachineDirectory()
        { return Acquire(Path.GetDirectoryName(FileLicenseStorage.DefaultPath), true); }
        private static WindowsLicensePath Acquire(string directory, bool prepare)
        {
            var scope = new WindowsLicensePath();
            try
            {
                var full = Path.GetFullPath(directory);
                var root = Path.GetPathRoot(full);
                if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType != DriveType.Fixed)
                    throw new UnsafeLicensePathException("Licensing requires a local fixed drive.");
                var current = root;
                scope.OpenDirectory(current);
                foreach (var part in full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    if (!Directory.Exists(current))
                    {
                        if (!prepare) throw new DirectoryNotFoundException("License directory missing.");
                        if (!string.Equals(current, full, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(current, Path.GetDirectoryName(full), StringComparison.OrdinalIgnoreCase))
                            throw new UnsafeLicensePathException("System common-data ancestors must already exist.");
                        // Create one level under an already locked parent; never follow an unchecked child.
                        Directory.CreateDirectory(current, DirectoryAcl());
                    }
                    if (prepare && (string.Equals(current, full, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(current, Path.GetDirectoryName(full), StringComparison.OrdinalIgnoreCase)))
                    {
                        using (var exclusive = OpenObject(current, true, 1))
                        {
                            ValidateDirectory(exclusive);
                            if (string.Equals(current, full, StringComparison.OrdinalIgnoreCase)) HardenLicensingDirectory(current);
                            else
                            {
                                var entries = OnlyExpectedEntry(current, "Licensing");
                                // Protect and preflight the child before parent ACL propagation can touch it.
                                if (entries.Length != 0) HardenLicensingDirectory(Path.Combine(current, "Licensing"));
                                Directory.SetAccessControl(current, DirectoryAcl());
                            }
                            scope.OpenDirectory(current);
                        }
                    }
                    else scope.OpenDirectory(current);
                }
                return scope;
            }
            catch { scope.Dispose(); throw; }
        }
        private static string[] OnlyExpectedEntry(string directory, string expected)
        {
            var entries = Directory.GetFileSystemEntries(directory);
            foreach (var entry in entries)
                if (!string.Equals(Path.GetFileName(entry), expected, StringComparison.OrdinalIgnoreCase))
                    throw new UnsafeLicensePathException("Unexpected entries in the dedicated licensing container; preserve and review them with support.");
            return entries;
        }
        private static void HardenLicensingDirectory(string directory)
        {
            using (var exclusive = OpenObject(directory, true, 1))
            {
                ValidateDirectory(exclusive);
                if (OnlyExpectedEntry(directory, "license.alklic").Length == 0) Directory.SetAccessControl(directory, DirectoryAcl());
                else
                {
                    var file = Path.Combine(directory, "license.alklic");
                    using (var handle = OpenObject(file, false, 1))
                    {
                        ValidateFile(handle);
                        Directory.SetAccessControl(directory, DirectoryAcl());
                        File.SetAccessControl(file, FileAcl());
                    }
                }
            }
        }
        private void OpenDirectory(string path)
        {
            var handle = OpenObject(path, true);
            try
            {
                ValidateDirectory(handle);
                handles.Add(handle);
            }
            catch { handle.Dispose(); throw; }
        }
        private static void ValidateDirectory(SafeFileHandle handle)
        {
            var info = Information(handle);
            if ((info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
                throw new UnsafeLicensePathException("Reparse points/non-directory ancestors are forbidden.");
        }
        public static void InspectFile(string path)
        { using (var handle = OpenObject(path, false)) ValidateFile(handle); }
        private static void ValidateFile(SafeFileHandle handle)
        {
            var info = Information(handle);
            if ((info.Attributes & (0x400 | 0x10 | 0x40)) != 0 || info.NumberOfLinks != 1)
                throw new UnsafeLicensePathException("License must be an ordinary file without reparse points/hard links.");
            if (info.SizeHigh != 0 || info.SizeLow > LicenseFormat.MaximumLength)
                throw new UnsafeLicensePathException("Existing license file is oversized.");
        }
        private static SafeFileHandle OpenObject(string path, bool directory, uint share = 3)
        {
            var handle = CreateFile(path, 0x80000000, share, IntPtr.Zero, 3, 0x00200000 | 0x02000000, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error == 5) throw new UnauthorizedAccessException("Access denied while securing license path.");
            throw new IOException("Cannot lock license path (Win32 " + error + ").", new Win32Exception(error));
        }
        private static FileInformation Information(SafeFileHandle handle)
        {
            FileInformation info;
            if (!GetFileInformationByHandle(handle, out info)) throw new IOException("Cannot inspect license path.", new Win32Exception(Marshal.GetLastWin32Error()));
            return info;
        }
        private static DirectorySecurity DirectoryAcl()
        {
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.BuiltinUsersSid })
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), sid == WellKnownSidType.BuiltinUsersSid ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            return acl;
        }
        internal static FileSecurity FileAcl()
        {
            var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.BuiltinUsersSid })
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), sid == WellKnownSidType.BuiltinUsersSid ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl, AccessControlType.Allow));
            return acl;
        }
        public void Dispose() { for (var index = handles.Count - 1; index >= 0; index--) handles[index].Dispose(); handles.Clear(); }
        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
            public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    }
    public sealed class MachineLicenseStorage : IPrivilegedLicenseStorage
    {
        private readonly FileLicenseStorage file = new FileLicenseStorage(FileLicenseStorage.DefaultPath);
        public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        public string Read() => file.Read();
        public void Write(string text)
        {
            if (!IsElevated) throw new UnauthorizedAccessException("Elevated administrator token required.");
            file.Write(text, WindowsLicensePath.FileAcl());
        }
        public IDisposable Prepare()
        {
            if (!IsElevated) throw new UnauthorizedAccessException("Elevated administrator token required.");
            return WindowsLicensePath.PrepareMachineDirectory();
        }
    }
}
