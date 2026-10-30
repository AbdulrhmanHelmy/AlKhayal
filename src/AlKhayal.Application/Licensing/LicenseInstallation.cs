using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AlKhayal.Application.Licensing
{
    public enum LicenseInstallResult { Success = 0, Cancelled = 10, AccessDenied = 11, InvalidLicense = 12, StorageFailure = 13, UnsafePath = 14, HelperUnavailable = 15 }
    public interface ILicenseInstaller { Task<LicenseInstallResult> InstallAsync(string license); }
    public interface IPrivilegedLicenseStorage : ILicenseStorage
    {
        bool IsElevated { get; }
        IDisposable Prepare();
    }
    public sealed class UnsafeLicensePathException : IOException
    {
        public UnsafeLicensePathException(string message) : base(message) { }
    }
    // No input file/path, shell command, environment switch or trust override enters this protocol.
    public static class LicenseInstallCommand
    {
        public const string Switch = "--install-license-v1";
        public static string Encode(string license)
        {
            LicenseFormat.ParseLicense(license);
            return Convert.ToBase64String(LicenseFormat.Bytes(license));
        }
        public static string Decode(string[] args)
        {
            if (args == null || args.Length != 2 || args[0] != Switch || args[1] == null || args[1].Length > 10924) throw new FormatException("Invalid installation command.");
            var bytes = Convert.FromBase64String(args[1]);
            if (bytes.Length > LicenseFormat.MaximumLength || Convert.ToBase64String(bytes) != args[1]) throw new FormatException("Invalid installation envelope.");
            var text = new UTF8Encoding(false, true).GetString(bytes);
            LicenseFormat.ParseLicense(text);
            return text;
        }
    }
    public sealed class LicenseInstallationOperation
    {
        private readonly IPrivilegedLicenseStorage storage;
        private readonly LicenseService service;
        public LicenseInstallationOperation(IPrivilegedLicenseStorage storage, ILicenseSignatures signatures, IHardwareIdentity hardware)
        { this.storage = storage; service = new LicenseService(storage, signatures, hardware); }
        public LicenseInstallResult Install(string text)
        {
            if (!storage.IsElevated) return LicenseInstallResult.AccessDenied;
            if (!service.Validate(text).IsValid) return LicenseInstallResult.InvalidLicense;
            try
            {
                using (storage.Prepare())
                {
                    // Never replace a valid installed entitlement, including on repeated activation.
                    if (service.Check().IsValid) return LicenseInstallResult.Success;
                    return service.Activate(text).IsValid ? LicenseInstallResult.Success : LicenseInstallResult.StorageFailure;
                }
            }
            catch (UnsafeLicensePathException) { return LicenseInstallResult.UnsafePath; }
            catch (UnauthorizedAccessException) { return LicenseInstallResult.AccessDenied; }
            catch (IOException) { return LicenseInstallResult.StorageFailure; }
        }
    }
    public sealed class LicenseActivationCoordinator
    {
        private readonly LicenseService service;
        private readonly ILicenseInstaller installer;
        public LicenseActivationCoordinator(LicenseService service, ILicenseInstaller installer) { this.service = service; this.installer = installer; }
        public async Task<LicenseStatus> ActivateAsync(string text)
        {
            var existing = service.Check();
            if (existing.IsValid) return existing;
            var validation = service.Validate(text);
            if (!validation.IsValid) return validation;
            var result = await installer.InstallAsync(text);
            if (result == LicenseInstallResult.Success)
            {
                var installed = service.Check();
                return installed.IsValid ? installed : new LicenseStatus(false, "لم يمكن التحقق من التفعيل المحفوظ. أعد المحاولة أو تواصل مع الدعم.");
            }
            return new LicenseStatus(false, Message(result));
        }
        public static string Message(LicenseInstallResult result)
        {
            switch (result)
            {
                case LicenseInstallResult.Cancelled: return "أُلغي طلب صلاحيات المسؤول. لم يُحفظ التفعيل؛ يمكنك إعادة المحاولة بالكود نفسه.";
                case LicenseInstallResult.AccessDenied: return "لم تُمنح صلاحيات المسؤول اللازمة لحفظ التفعيل. اطلب مساعدة مسؤول الجهاز ثم أعد المحاولة.";
                case LicenseInstallResult.InvalidLicense: return "رفض مثبت التفعيل الترخيص بعد التحقق المستقل. تحقق من الكود والجهاز.";
                case LicenseInstallResult.UnsafePath: return "مسار حفظ التفعيل غير آمن أو معاد توجيهه. تواصل مع الدعم دون تغيير صلاحيات المجلد.";
                case LicenseInstallResult.HelperUnavailable: return "تعذر تشغيل مثبت التفعيل. تحقق من ملفات البرنامج ثم أعد المحاولة.";
                default: return "تعذر حفظ التفعيل بأمان. لم يكتمل التثبيت؛ أعد المحاولة أو تواصل مع الدعم.";
            }
        }
    }
}
