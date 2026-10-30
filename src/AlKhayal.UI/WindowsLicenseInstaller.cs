using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;

namespace AlKhayal.UI
{
    public sealed class WindowsLicenseInstaller : ILicenseInstaller
    {
        public Task<LicenseInstallResult> InstallAsync(string license)
        {
            // ShellExecute may wait for consent/credentials. Keep the dialog thread responsive.
            return Task.Run(() =>
            {
                if (new MachineLicenseStorage().IsElevated) return LicenseHelperEntry.Install(license);
                try
                {
                    var start = new ProcessStartInfo
                    {
                        FileName = System.Windows.Forms.Application.ExecutablePath,
                        Arguments = LicenseInstallCommand.Switch + " " + LicenseInstallCommand.Encode(license),
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    };
                    using (var helper = Process.Start(start))
                    {
                        if (helper == null) return LicenseInstallResult.HelperUnavailable;
                        helper.WaitForExit();
                        return Enum.IsDefined(typeof(LicenseInstallResult), helper.ExitCode)
                            ? (LicenseInstallResult)helper.ExitCode : LicenseInstallResult.HelperUnavailable;
                    }
                }
                catch (Win32Exception error) { return MapWin32Error(error.NativeErrorCode); }
                catch (UnauthorizedAccessException) { return LicenseInstallResult.AccessDenied; }
                catch (InvalidOperationException) { return LicenseInstallResult.HelperUnavailable; }
            });
        }
        public static LicenseInstallResult MapWin32Error(int error)
        {
            if (error == 1223) return LicenseInstallResult.Cancelled;
            if (error == 5 || error == 740 || error == 1326 || error == 1314) return LicenseInstallResult.AccessDenied;
            return LicenseInstallResult.HelperUnavailable;
        }
    }
    internal static class LicenseHelperEntry
    {
        internal static LicenseInstallResult Install(string license)
        {
            var target = new MachineLicenseStorage();
            if (!target.IsElevated) return LicenseInstallResult.AccessDenied;
            // Fresh verifier and fresh WMI identity in the elevated process, never caller-supplied trust/claims.
            return new LicenseInstallationOperation(target, RsaLicenseSignatures.Embedded(), new WindowsHardwareIdentity()).Install(license);
        }
        internal static int Execute(string[] args)
        {
            try { return (int)Install(LicenseInstallCommand.Decode(args)); }
            catch (Exception error) when (error is FormatException || error is System.Text.DecoderFallbackException)
            { return (int)LicenseInstallResult.InvalidLicense; }
            catch (Exception error)
            {
                // No command-line arguments or signed payload is logged.
                Trace.TraceError("License helper failed: {0} (0x{1:X8})", error.GetType().Name, error.HResult);
                return (int)LicenseInstallResult.StorageFailure;
            }
        }
    }
}
