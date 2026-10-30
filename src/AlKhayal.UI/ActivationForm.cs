using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;
using AlKhayal.Application.Licensing;
namespace AlKhayal.UI
{
    public sealed class ActivationForm : Form
    {
        private readonly LicenseService service;
        private readonly LicenseActivationCoordinator activation;
        private Button activationButton;
        // Selectable display only: excluded from the action/tab sequence.
        private readonly TextBox request = new TextBox { Multiline = true, ReadOnly = true, TabStop = false, Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, BorderStyle = BorderStyle.FixedSingle, AccessibleRole = AccessibleRole.StaticText, AccessibleName = "كود الجهاز" };
        private readonly TextBox license = new TextBox { Multiline = true, MaxLength = LicenseFormat.MaximumLength, Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "كود التفعيل", TabIndex = 1 };
        private readonly Label message = new Label { Dock = DockStyle.Fill, ForeColor = Color.Maroon, AutoEllipsis = false, AccessibleRole = AccessibleRole.Alert };
        public ActivationForm(LicenseService service, ILicenseInstaller installer = null)
        {
            this.service = service;
            activation = new LicenseActivationCoordinator(service, installer ?? new WindowsLicenseInstaller());
            Text = "تفعيل برنامج الخيال"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(620, 365); MinimumSize = new Size(580, 400); MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250); ForeColor = Color.FromArgb(18, 35, 58);
            request.BackColor = Color.FromArgb(236, 240, 245); request.Font = new Font("Consolas", 8.5f); license.Font = request.Font;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
            foreach (var height in new[] { 27, 80, 34, 23, 82, 34, 50 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.Controls.Add(new Label { Text = "التفعيل مجاني ودائم لهذا الجهاز.", AutoSize = true }, 0, 0);
            layout.Controls.Add(request, 0, 1);
            var copy = CreateButton("نسخ كود الجهاز", 0, () => { if (request.Text.Length > 0) { Clipboard.SetText(request.Text); message.Text = "تم نسخ كود الجهاز كاملًا."; } return Task.CompletedTask; });
            layout.Controls.Add(copy, 0, 2);
            layout.Controls.Add(new Label { Text = "كود التفعيل", AutoSize = true }, 0, 3);
            layout.Controls.Add(license, 0, 4);
            var activate = activationButton = CreateButton("تفعيل البرنامج", 2, ActivateLicenseAsync);
            layout.Controls.Add(activate, 0, 5); layout.Controls.Add(message, 0, 6); Controls.Add(layout);
            message.Text = service.Check().Message;
            try { request.Text = service.CreateRequest(); }
            catch (FormatException e) { message.Text = e.Message; copy.Enabled = false; activate.Enabled = false; }
        }
        private Button CreateButton(string text, int tabIndex, Func<Task> action)
        {
            var button = new Button { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Height = 29, MinimumSize = new Size(120, 28), TabIndex = tabIndex, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(218, 185, 111), Margin = new Padding(0, 2, 0, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(191, 159, 89);
            button.Click += async (s, e) =>
            {
                try { await action(); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is System.Runtime.InteropServices.ExternalException)
                { message.Text = "تعذر إكمال العملية: " + error.Message; }
            };
            return button;
        }
        private async Task ActivateLicenseAsync()
        {
            activationButton.Enabled = false;
            message.Text = "جارٍ التحقق وحفظ التفعيل. قد يطلب Windows صلاحيات المسؤول لهذه العملية فقط.";
            try
            {
                var result = await activation.ActivateAsync(license.Text.Trim());
                if (IsDisposed) return;
                message.Text = result.Message;
                if (result.IsValid) { DialogResult = DialogResult.OK; Close(); }
            }
            finally { if (!IsDisposed) activationButton.Enabled = true; }
        }
    }
}
