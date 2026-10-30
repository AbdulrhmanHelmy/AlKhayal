using AlKhayal.Application.Licensing;
namespace AlKhayal.IntegrationTests
{
    // Explicit fixture-only composition. Never compiled into customer assemblies.
    internal sealed class TestLicenseGate : ILicenseGate
    {
        public bool IsValid = true;
        public LicenseStatus Check() => new LicenseStatus(IsValid, "test fixture");
    }
}
