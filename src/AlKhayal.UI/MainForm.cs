using System;
using System.Drawing;
using System.IO;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlKhayal.Server.Host;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AlKhayal.UI
{
    public sealed class MainForm : Form
    {
        private readonly WebView2 _browser = new WebView2();
        private readonly Label _status = new Label();
        private AlKhayalServer _server;
        private readonly LicenseService _licensing;
        private FileSystemWatcher _licenseWatcher;
        private bool _activationOpen;

        public MainForm(LicenseService licensing)
        {
            _licensing = licensing ?? throw new ArgumentNullException(nameof(licensing));
            Text = "الخيال POS";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(900, 600);
            AutoScaleMode = AutoScaleMode.Dpi;
            RightToLeft = RightToLeft.Yes;
            BackColor = Color.White;

            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleCenter;
            _status.Text = "جارٍ الاتصال بالخادم…";
            Controls.Add(_status);

            Shown += async (sender, args) => await InitializeAsync();
            FormClosed += (sender, args) =>
            {
                _browser.Dispose();
                _licenseWatcher?.Dispose();
                _server?.Dispose();
            };
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (!_licensing.Check().IsValid) { Close(); return; }
                _licenseWatcher = new FileSystemWatcher(Path.GetDirectoryName(FileLicenseStorage.DefaultPath)) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Security };
                _licenseWatcher.Changed += LicenseChanged;
                _licenseWatcher.Deleted += LicenseChanged;
                _licenseWatcher.Created += LicenseChanged;
                _licenseWatcher.Renamed += LicenseChanged;
                _licenseWatcher.Error += (s, e) =>
                {
                    StartupFailureReporter.TryLog(e.GetException());
                    if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => RequireActivation(true)));
                };
                _licenseWatcher.EnableRaisingEvents = true;
                Activated += (s, e) => RequireActivation();
                var runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrWhiteSpace(runtime))
                {
                    throw new InvalidOperationException("لم يتم العثور على WebView2 Runtime.");
                }

                var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dataDirectory = WebView2UserDataFolderResolver.Resolve(
                    Environment.GetEnvironmentVariable("ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER"),
                    localApplicationDataPath);
                WebView2UserDataFolderResolver.EnsureDirectory(dataDirectory);

                var url = Environment.GetEnvironmentVariable("ALKHAYAL_SERVER_URL");
                if (string.IsNullOrWhiteSpace(url))
                {
                    _server = new AlKhayalServer(_licensing);
                    _server.Start(new HostOptions());
                    url = _server.Options.Url;
                }

                var allowedOrigin = new Uri(url, UriKind.Absolute);
                var environment = await CoreWebView2Environment.CreateAsync(null, dataDirectory);
                await _browser.EnsureCoreWebView2Async(environment);
                // Frontend files and the screen registry must be from the same deployment version.
                // This preserves cookies/local storage while preventing an older cached shell from
                // consuming a newer no-store screens.json and missing newly registered renderers.
                await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
                _browser.Dock = DockStyle.Fill;
                _browser.Margin = Padding.Empty;
                _browser.ZoomFactor = 1.0;

                var settings = _browser.CoreWebView2.Settings;
                settings.IsZoomControlEnabled = false;
                settings.IsPinchZoomEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = true;

                // This is scoped to the desktop WebView2 host. Browser users retain normal zoom accessibility.
                await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "document.addEventListener('wheel',function(e){if(e.ctrlKey)e.preventDefault();},{passive:false});" +
                    "document.addEventListener('keydown',function(e){if(e.ctrlKey && (e.key==='+' || e.key==='-' || e.key==='=' || e.key==='0'))e.preventDefault();},true);");

                _browser.CoreWebView2.NavigationStarting += (sender, args) =>
                {
                    var target = new Uri(args.Uri, UriKind.Absolute);
                    args.Cancel = target.Scheme != allowedOrigin.Scheme
                        || !string.Equals(target.Host, allowedOrigin.Host, StringComparison.OrdinalIgnoreCase)
                        || target.Port != allowedOrigin.Port;
                };

                if (!_licensing.Check().IsValid) RequireActivation();
                if (IsDisposed) return;
                Controls.Clear();
                Controls.Add(_browser);
                _browser.Source = new Uri(allowedOrigin, "login.html");
            }
            catch (Exception exception)
            {
                var logged = StartupFailureReporter.TryLog(exception);
                _server?.Dispose();
                _server = null;
                Controls.Clear();
                Controls.Add(_status);
                _status.Text = logged
                    ? "تعذر بدء تشغيل التطبيق. يرجى التواصل مع الدعم الفني؛ حُفظت تفاصيل المشكلة في سجل التشخيص."
                    : "تعذر بدء تشغيل التطبيق. يرجى التواصل مع الدعم الفني.";
            }
        }

        private void LicenseChanged(object sender, FileSystemEventArgs args)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => RequireActivation()));
        }

        private void RequireActivation(bool watcherFailed = false)
        {
            if (_activationOpen || IsDisposed) return;
            if (!watcherFailed && _licensing.Check().IsValid) return;
            _activationOpen = true;
            _browser.Visible = false;
            try
            {
                if (watcherFailed)
                {
                    MessageBox.Show(this, "تعذر مراقبة ملف التفعيل بأمان. أعد تشغيل البرنامج؛ وإذا تكررت المشكلة تواصل مع الدعم.", "التفعيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close(); return;
                }
                using (var activation = new ActivationForm(_licensing))
                    if (activation.ShowDialog(this) != DialogResult.OK || !_licensing.Check().IsValid) { Close(); return; }
                if (_browser.CoreWebView2 != null && _browser.Source != null)
                    _browser.CoreWebView2.Navigate(new Uri(new Uri(_browser.Source.GetLeftPart(UriPartial.Authority)), "login.html").AbsoluteUri);
                _browser.Visible = true;
            }
            finally { _activationOpen = false; }
        }
    }
}
