using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Licensing;

namespace AlKhayal.Api
{
    public sealed class LicenseAccessHandler : DelegatingHandler
    {
        private readonly ILicenseGate gate;
        public LicenseAccessHandler(ILicenseGate gate) { this.gate = gate; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!gate.Check().IsValid)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(
                    "{\"success\":false,\"data\":null,\"error\":{\"code\":\"activation_required\",\"message\":\"يلزم تفعيل البرنامج على هذا الجهاز قبل استخدامه. التفعيل مجاني ودائم.\"}}",
                    System.Text.Encoding.UTF8, "application/json") });
            return base.SendAsync(request, cancellationToken);
        }
    }
}
