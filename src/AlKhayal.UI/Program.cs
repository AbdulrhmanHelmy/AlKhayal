using System;
using System.Windows.Forms;
using AlKhayal.Data.Licensing;
namespace AlKhayal.UI
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Narrow helper mode exits before UI/WebView2/host/database composition.
            if (args.Length > 0 && args[0] == AlKhayal.Application.Licensing.LicenseInstallCommand.Switch)
                return LicenseHelperEntry.Execute(args);
            if (args.Length > 0 && (args.Length != 1 || args[0] != "--initialize-license-storage"))
                return (int)AlKhayal.Application.Licensing.LicenseInstallResult.InvalidLicense;
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 1 && args[0] == "--initialize-license-storage")
                {
                    FileLicenseStorage.InitializeMachineDirectory();
                    MessageBox.Show("تم إعداد مجلد التراخيص. شغّل البرنامج بصورة عادية؛ سيطلب صلاحية حفظ التفعيل عند الحاجة فقط."); return 0;
                }
                var licensing = MachineLicensing.Create();
                while (!licensing.Check().IsValid)
                    using (var dialog = new ActivationForm(licensing))
                        if (dialog.ShowDialog() != DialogResult.OK) return 0;
                if (!licensing.Check().IsValid) return 0;
                System.Windows.Forms.Application.Run(new MainForm(licensing));
            }
            catch (Exception e)
            {
                StartupFailureReporter.TryLog(e);
                MessageBox.Show("تعذر بدء البرنامج. تحقق من إعداد مجلد التراخيص وصلاحياته وتواصل مع الدعم.", "الخيال", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 0;
        }
    }
}
