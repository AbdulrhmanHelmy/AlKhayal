using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using AlKhayal.UI;
using Xunit;
using Xunit.Abstractions;
namespace AlKhayal.UnitTests
{
    public sealed class LicenseInstallationTests : IDisposable
    {
        private readonly System.Security.Cryptography.RSACryptoServiceProvider rsa=RsaLicenseSignatures.CreateRsa();
        private readonly string root=Path.Combine(Path.GetTempPath(),"alk-uac-test-"+Guid.NewGuid().ToString("N"));
        private readonly Hardware hardware=new Hardware();
        private readonly ITestOutputHelper output;
        public LicenseInstallationTests(ITestOutputHelper output){this.output=output;Directory.CreateDirectory(root);}
        public void Dispose(){rsa.Dispose();Directory.Delete(root,true);}
        private RsaLicenseSignatures Signatures()=>new RsaLicenseSignatures(new Dictionary<string,string>{{"disposable-install-test",rsa.ToXmlString(false)}});
        private string License()=>Sign(LicenseFormat.CreatePayload(hardware.ReadClaims(),"disposable-install-test",Guid.NewGuid(),DateTime.UtcNow));
        private string Sign(string payload)=>payload+"|"+Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload),"SHA256"));
        private Target NewTarget()=>new Target(Path.Combine(root,"Licensing"));
        [Theory][InlineData("missing")][InlineData("initialized")][InlineData("corrupt")]
        public async Task Simulated_elevated_install_clean_initialized_corrupt_recovery_and_restart(string state)
        {
            var target=NewTarget();if(state!="missing")Directory.CreateDirectory(target.Directory);
            if(state=="corrupt")File.WriteAllText(target.Path,"corrupt");
            var service=new LicenseService(target,Signatures(),hardware);
            Assert.False(service.Check().IsValid);
            var helper=new OperationInstaller(new LicenseInstallationOperation(target,Signatures(),hardware));
            Assert.True((await new LicenseActivationCoordinator(service,helper).ActivateAsync(License())).IsValid);
            Assert.True(new LicenseService(target,Signatures(),hardware).Check().IsValid);Assert.Equal(1,helper.Calls);Assert.Equal(1,target.Writes);
            Assert.Single(Directory.GetFiles(target.Directory));
        }
        [Theory][InlineData(LicenseInstallResult.Cancelled)][InlineData(LicenseInstallResult.AccessDenied)][InlineData(LicenseInstallResult.HelperUnavailable)][InlineData(LicenseInstallResult.StorageFailure)]
        public async Task Simulated_UAC_cancel_denial_missing_credentials_or_helper_failure_never_unlocks(LicenseInstallResult result)
        {
            var target=NewTarget();var service=new LicenseService(target,Signatures(),hardware);var installer=new ResultInstaller(result);
            var status=await new LicenseActivationCoordinator(service,installer).ActivateAsync(License());
            Assert.False(status.IsValid);Assert.False(service.Check().IsValid);Assert.Equal(0,target.Writes);Assert.False(Directory.Exists(target.Directory));Assert.NotEmpty(status.Message);
        }
        [Fact] public async Task Successful_helper_exit_without_valid_persisted_file_is_not_accepted()
        {
            var target=NewTarget();var service=new LicenseService(target,Signatures(),hardware);
            Assert.False((await new LicenseActivationCoordinator(service,new ResultInstaller(LicenseInstallResult.Success)).ActivateAsync(License())).IsValid);
        }
        [Fact] public async Task Existing_activation_skips_installer_and_different_valid_reimport_preserves_original_bytes()
        {
            var target=NewTarget();Directory.CreateDirectory(target.Directory);var original=License();target.Write(original);target.Writes=0;
            var service=new LicenseService(target,Signatures(),hardware);var installer=new ResultInstaller(LicenseInstallResult.AccessDenied);
            Assert.True((await new LicenseActivationCoordinator(service,installer).ActivateAsync("unused")).IsValid);Assert.Equal(0,installer.Calls);
            Assert.Equal(LicenseInstallResult.Success,new LicenseInstallationOperation(target,Signatures(),hardware).Install(License()));
            Assert.Equal(original,target.Read());Assert.Equal(0,target.Writes);
        }
        [Theory][InlineData("signature")][InlineData("key")][InlineData("product")][InlineData("schema")][InlineData("hardware")]
        public async Task Main_and_helper_independently_reject_untrusted_wrong_device_and_modified_input(string kind)
        {
            var target=NewTarget();var text=License();var parts=text.Split('|');
            if(kind=="signature")parts[10]=Convert.ToBase64String(new byte[384]);
            if(kind=="key")parts[8]="unknown";if(kind=="product")parts[2]="OtherPOS";if(kind=="schema")parts[1]="2";
            if(kind!="hardware")text=string.Join("|",parts);
            var other=new Hardware { Other=kind=="hardware" };
            var service=new LicenseService(target,Signatures(),other);var installer=new ResultInstaller(LicenseInstallResult.Success);
            Assert.False((await new LicenseActivationCoordinator(service,installer).ActivateAsync(text)).IsValid);Assert.Equal(0,installer.Calls);
            Assert.Equal(LicenseInstallResult.InvalidLicense,new LicenseInstallationOperation(target,Signatures(),other).Install(text));
            Assert.Equal(0,target.Prepares);Assert.Equal(0,target.Writes);
        }
        [Fact] public void Helper_requires_elevated_authority_before_provisioning()
        {
            var target=NewTarget();target.IsElevated=false;
            Assert.Equal(LicenseInstallResult.AccessDenied,new LicenseInstallationOperation(target,Signatures(),hardware).Install(License()));Assert.Equal(0,target.Prepares);
        }
        [Theory][InlineData("acl")][InlineData("write")][InlineData("unsafe")]
        public void Simulated_ACL_io_and_unsafe_path_failures_remain_locked_without_partial_files(string failure)
        {
            var target=NewTarget();target.Failure=failure;var operation=new LicenseInstallationOperation(target,Signatures(),hardware);
            var result=operation.Install(License());Assert.NotEqual(LicenseInstallResult.Success,result);Assert.False(new LicenseService(target,Signatures(),hardware).Check().IsValid);
            if(Directory.Exists(target.Directory))Assert.Empty(Directory.GetFiles(target.Directory));
        }
        [Fact] public void Actual_medium_token_write_denied_at_CreateNew_temp_with_secure_directory_ACL()
        {
            var elevated=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            output.WriteLine("Actual current Windows token administrator enabled: "+elevated);
            if(elevated){output.WriteLine("Restricted write reproduction UNVERIFIED in this elevated run.");return;}
            var directory=Path.Combine(root,"ReadOnlyLicense");Directory.CreateDirectory(directory);FileLicenseStorage.InitializeDirectory(directory);
            try
            {
                var error=Assert.Throws<UnauthorizedAccessException>(()=>new FileLicenseStorage(Path.Combine(directory,"license.alklic")).Write(License()));
                output.WriteLine("Actual CreateNew temporary-file failure: "+error.GetType().Name+" HRESULT=0x"+error.HResult.ToString("X8"));
                Assert.Equal(unchecked((int)0x80070005),error.HResult);Assert.Empty(Directory.GetFiles(directory));
                Assert.Contains("FileStream",error.StackTrace);
            }
            finally
            {
                var acl=Directory.GetAccessControl(directory);acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(directory,acl);
            }
        }
        [Fact] public void Actual_atomic_replace_failure_retains_previous_file_and_removes_only_own_temporary_file()
        {
            var target=NewTarget();Directory.CreateDirectory(target.Directory);var old=License();target.Write(old);
            using(var locked=new FileStream(target.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
                Assert.Throws<IOException>(()=>target.Write(License()));
            Assert.Equal(old,target.Read());Assert.Single(Directory.GetFiles(target.Directory));
        }
        [Fact] public void Command_envelope_has_no_destination_or_executable_arguments_and_rejects_manipulation()
        {
            var text=License();var encoded=LicenseInstallCommand.Encode(text);Assert.Equal(text,LicenseInstallCommand.Decode(new[]{LicenseInstallCommand.Switch,encoded}));
            Assert.Throws<FormatException>(()=>LicenseInstallCommand.Decode(new[]{LicenseInstallCommand.Switch,encoded,"--path",root}));
            Assert.Throws<FormatException>(()=>LicenseInstallCommand.Decode(new[]{"--other",encoded}));
            Assert.Throws<FormatException>(()=>LicenseInstallCommand.Decode(new[]{LicenseInstallCommand.Switch,"not a file path"}));
            Assert.Throws<FormatException>(()=>LicenseInstallCommand.Decode(new[]{LicenseInstallCommand.Switch,new string('A',10925)}));
        }
        [Theory][InlineData(1223,LicenseInstallResult.Cancelled)][InlineData(5,LicenseInstallResult.AccessDenied)][InlineData(740,LicenseInstallResult.AccessDenied)][InlineData(1326,LicenseInstallResult.AccessDenied)][InlineData(1314,LicenseInstallResult.AccessDenied)][InlineData(2,LicenseInstallResult.HelperUnavailable)]
        public void Win32_UAC_result_mapping_is_explicit(int code,LicenseInstallResult expected)=>Assert.Equal(expected,WindowsLicenseInstaller.MapWin32Error(code));
        [Fact] public void Actual_helper_entry_rejects_bad_arguments_before_any_storage_or_WMI_composition()
        {
            Assert.Equal((int)LicenseInstallResult.InvalidLicense,LicenseHelperEntry.Execute(new[]{LicenseInstallCommand.Switch,"invalid"}));
            Assert.Equal((int)LicenseInstallResult.InvalidLicense,LicenseHelperEntry.Execute(new[]{LicenseInstallCommand.Switch,LicenseInstallCommand.Encode(License()),"--path",root}));
            if(!new MachineLicenseStorage().IsElevated)
                Assert.Equal((int)LicenseInstallResult.AccessDenied,LicenseHelperEntry.Execute(new[]{LicenseInstallCommand.Switch,LicenseInstallCommand.Encode(License())}));
        }
        [Fact] public void Actual_directory_locks_prevent_rename_and_dispose_releases_handles()
        {
            var directory=Path.Combine(root,"locked");Directory.CreateDirectory(directory);
            using(WindowsLicensePath.InspectDirectory(directory))Assert.Throws<IOException>(()=>Directory.Move(directory,directory+"-moved"));
            Directory.Move(directory,directory+"-moved");
        }
        [Fact] public void Actual_junction_ancestor_is_rejected_without_following_or_writing_destination()
        {
            var destination=Path.Combine(root,"destination");Directory.CreateDirectory(Path.Combine(destination,"Licensing"));var junction=Path.Combine(root,"junction");
            var script="New-Item -ItemType Junction -Path '"+junction.Replace("'","''")+"' -Target '"+destination.Replace("'","''")+"' | Out-Null";
            using(var process=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -Command \""+script+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}))
            {process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();Assert.True(process.WaitForExit(15000));Assert.True(process.ExitCode==0,error);}
            try{Assert.Throws<UnsafeLicensePathException>(()=>WindowsLicensePath.InspectDirectory(Path.Combine(junction,"Licensing")));Assert.Empty(Directory.GetFiles(destination,"*",SearchOption.AllDirectories));}
            finally{Directory.Delete(junction);}
        }
        [Fact] public void Actual_hard_links_and_non_file_targets_are_rejected()
        {
            var path=Path.Combine(root,"original");var link=Path.Combine(root,"link");File.WriteAllText(path,"disposable");Assert.True(CreateHardLink(link,path,IntPtr.Zero));
            Assert.Throws<UnsafeLicensePathException>(()=>WindowsLicensePath.InspectFile(link));Assert.Equal("disposable",File.ReadAllText(path));
            Assert.Throws<UnsafeLicensePathException>(()=>WindowsLicensePath.InspectFile(root));
            Assert.Throws<UnsafeLicensePathException>(()=>WindowsLicensePath.InspectDirectory(@"\\nonexistent\share\Licensing"));
        }
        private sealed class Hardware:IHardwareIdentity {public bool Other;public IDictionary<string,string> ReadClaims()=>LicenseFormat.HashClaims(new Dictionary<string,string>{{"uuid",Other?"other-uac-uuid":"uac-test-uuid"},{"board",Other?"other-uac-board":"uac-test-board"},{"bios","uac-test-bios"}});}
        private sealed class Target:IPrivilegedLicenseStorage
        {
            public string Directory,Path,Failure;public int Writes,Prepares;public bool IsElevated{get;set;}=true;
            public Target(string directory){Directory=directory;Path=System.IO.Path.Combine(directory,"license.alklic");}
            public string Read()=>new FileLicenseStorage(Path).Read();
            public void Write(string text){if(Failure=="write")throw new IOException("Simulated disk failure");new FileLicenseStorage(Path).Write(text);Writes++;}
            public IDisposable Prepare(){Prepares++;if(Failure=="acl")throw new UnauthorizedAccessException("Simulated ACL failure");if(Failure=="unsafe")throw new UnsafeLicensePathException("Simulated unsafe path");System.IO.Directory.CreateDirectory(Directory);return WindowsLicensePath.InspectDirectory(Directory);}
        }
        private sealed class OperationInstaller:ILicenseInstaller {private readonly LicenseInstallationOperation operation;public int Calls;public OperationInstaller(LicenseInstallationOperation operation){this.operation=operation;}public Task<LicenseInstallResult> InstallAsync(string text){Calls++;return Task.FromResult(operation.Install(text));}}
        private sealed class ResultInstaller:ILicenseInstaller {private readonly LicenseInstallResult result;public int Calls;public ResultInstaller(LicenseInstallResult result){this.result=result;}public Task<LicenseInstallResult> InstallAsync(string text){Calls++;return Task.FromResult(result);}}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateHardLinkW")]private static extern bool CreateHardLink(string link,string existing,IntPtr security);
    }
}
