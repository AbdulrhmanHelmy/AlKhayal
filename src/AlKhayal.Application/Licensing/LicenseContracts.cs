using System;
using System.Collections.Generic;

namespace AlKhayal.Application.Licensing
{
    public interface ILicenseGate { LicenseStatus Check(); }
    public interface IHardwareIdentity { IDictionary<string, string> ReadClaims(); }
    public interface ILicenseStorage { string Read(); void Write(string text); }
    public interface ILicenseSignatures { bool Verify(string keyId, byte[] payload, byte[] signature); }

    public sealed class LicenseStatus
    {
        public bool IsValid { get; }
        public string Message { get; }
        public LicenseStatus(bool valid, string message) { IsValid = valid; Message = message; }
    }

    public sealed class LicenseService : ILicenseGate
    {
        private readonly ILicenseStorage storage;
        private readonly ILicenseSignatures signatures;
        private readonly IDictionary<string, string> claims;
        private readonly object validationLock = new object();
        private string verifiedText;
        public LicenseService(ILicenseStorage storage, ILicenseSignatures signatures, IHardwareIdentity hardware)
        {
            this.storage = storage; this.signatures = signatures;
            claims = new Dictionary<string, string>(hardware.ReadClaims());
        }
        public string CreateRequest() => LicenseFormat.CreateRequest(claims);
        public LicenseStatus Check()
        {
            try { return Validate(storage.Read()); }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            { return new LicenseStatus(false, "تعذر قراءة ملف التفعيل. تحقق من صلاحيات الملف أو تواصل مع الدعم."); }
        }
        public LicenseStatus Validate(string text)
        {
            if (string.IsNullOrEmpty(text)) return new LicenseStatus(false, "لم يتم تفعيل هذا الجهاز بعد. التفعيل مجاني ودائم.");
            lock (validationLock)
            {
            try
            {
                if (text != null && text == verifiedText) return new LicenseStatus(true, "تم التفعيل المجاني والدائم بنجاح.");
                var license = LicenseFormat.ParseLicense(text);
                if (!signatures.Verify(license.SigningKeyId, LicenseFormat.Bytes(license.Payload), license.Signature))
                    return new LicenseStatus(false, "مفتاح التوقيع غير موثوق أو توقيع الترخيص غير صحيح.");
                if (!LicenseFormat.Matches(license.Claims, claims))
                    return new LicenseStatus(false, "الترخيص لجهاز آخر أو تغيرت هوية الجهاز. اطلب تفعيلًا بديلًا مجانيًا.");
                verifiedText = text;
                return new LicenseStatus(true, "تم التفعيل المجاني والدائم بنجاح.");
            }
            catch (FormatException e) { return new LicenseStatus(false, e.Message); }
            }
        }
        public LicenseStatus Activate(string text)
        {
            var status = Validate(text);
            if (!status.IsValid) return status;
            try { storage.Write(text); return Check(); }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            { return new LicenseStatus(false, "تعذر حفظ التفعيل. تحقق من صلاحيات مجلد التراخيص ثم أعد المحاولة."); }
        }
    }
}
