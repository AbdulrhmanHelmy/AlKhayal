using System;
using System.Drawing;
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

        public MainForm()
        {
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
                _server?.Dispose();
            };
        }

        private async Task InitializeAsync()
        {
            try
            {
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
                    _server = new AlKhayalServer();
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

                Controls.Clear();
                Controls.Add(_browser);
                _browser.Source = new Uri(allowedOrigin, "login.html");
            }
            catch (Exception exception)
            {
                _status.Text = "تعذر الاتصال بالخادم أو WebView2. ثبّت Runtime المدعوم وتحقق من إعدادات التشغيل.\r\n" + exception.Message;
            }
        }
    }
}
