using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using AlKhayal.Application.Licensing;

namespace AlKhayal.Data.Licensing
{
    public sealed class WindowsHardwareIdentity : IHardwareIdentity
    {
        public IDictionary<string, string> ReadClaims()
        {
            var raw = new Dictionary<string, string>();
            Read(raw, "uuid", "Win32_ComputerSystemProduct", "UUID");
            Read(raw, "board", "Win32_BaseBoard", "SerialNumber");
            Read(raw, "bios", "Win32_BIOS", "SerialNumber");
            return LicenseFormat.HashClaims(raw);
        }
        private static void Read(IDictionary<string, string> raw, string name, string table, string property)
        {
            try
            {
                var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(5), ReturnImmediately = false };
                using (var search = new ManagementObjectSearcher("root\\CIMV2", "SELECT " + property + " FROM " + table, options))
                using (var rows = search.Get())
                    foreach (ManagementBaseObject row in rows)
                        using (row)
                        {
                            var value = LicenseFormat.Normalize(Convert.ToString(row[property]));
                            if (value != null) { raw[name] = value; break; }
                        }
            }
            catch (Exception e) when (e is ManagementException || e is COMException || e is UnauthorizedAccessException || e is System.Runtime.InteropServices.InvalidComObjectException)
            { Trace.TraceWarning("Licensing hardware source unavailable: {0} ({1})", name, e.GetType().Name); }
        }
    }
}
