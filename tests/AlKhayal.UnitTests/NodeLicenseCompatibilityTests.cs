using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using Xunit;
namespace AlKhayal.UnitTests
{
    public sealed class NodeLicenseCompatibilityTests
    {
        [Fact]
        public async System.Threading.Tasks.Task DotNet_request_Node_SQLite_issuance_DotNet_activation_restart_wrong_device_and_tampering()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName,"AlKhayal.slnx"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var root = Path.Combine(Path.GetTempPath(),"alk-node-cross-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var hardware = new Hardware();
                var request = LicenseFormat.CreateRequest(hardware.ReadClaims()); var requestFile=Path.Combine(root,"device.alkreq"); File.WriteAllText(requestFile,request,new UTF8Encoding(false));
                var node=Environment.GetEnvironmentVariable("ALKHAYAL_LICENSE_MANAGER_NODE") ?? "node.exe";
                var script=Path.Combine(directory.FullName,"tools","AlKhayal.LicenseManager","tests","cross-sign.cjs");
                var start=new ProcessStartInfo(node,"\""+script+"\" \""+root+"\" \""+requestFile+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var process=Process.Start(start))
                {
                    var output=process.StandardOutput.ReadToEndAsync(); var errors=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(30000)){process.Kill();throw new Exception("Node cross-component test timed out.");}
                    Assert.True(process.ExitCode==0,await errors); await output;
                }
                var result=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"cross-output.json")));
                var signatures=new RsaLicenseSignatures(new Dictionary<string,string>{{result["keyId"],result["publicXml"]}});
                var storage=new FileLicenseStorage(Path.Combine(root,"active.alklic"));var service=new LicenseService(storage,signatures,hardware);
                Assert.True(service.Activate(result["license"]).IsValid);
                Assert.True(new LicenseService(storage,signatures,hardware).Check().IsValid);
                Assert.False(new LicenseService(storage,signatures,new Hardware{Other=true}).Validate(result["license"]).IsValid);
                Assert.False(service.Validate(result["license"].Replace("Perpetual","Trial")).IsValid);
                var p=result["license"].Split('|');p[3]=Guid.NewGuid().ToString("D");Assert.False(service.Validate(string.Join("|",p)).IsValid);
                Assert.Equal("rsa-"+LicenseFormat.Hash(result["publicXml"]).Substring(0,24),result["keyId"]);
            }
            finally { Directory.Delete(root,true); }
        }
        private sealed class Hardware:IHardwareIdentity
        {
            public bool Other;
            public IDictionary<string,string> ReadClaims()=>LicenseFormat.HashClaims(new Dictionary<string,string>{{"uuid",Other?"other-fake-uuid":"cross-fake-uuid"},{"board",Other?"other-fake-board":"cross-fake-board"},{"bios","cross-fake-bios"}});
        }
    }
}
