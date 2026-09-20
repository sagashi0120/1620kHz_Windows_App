using System.Drawing;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _1620kHz_Windows_App
{
    public class VersionForm : Form
    {
        private const string API_URL = "https://highwayradio.cloudfree.jp/api/web-app/version";
        private const string DOWNLOAD_BASE =
            "https://github.com/sagashi0120/1620kHz_Windows_App/releases/download";

        private readonly string _currentVersion;
        private readonly string _currentVersionNumeric;

        private Label lblCurrent = null!;
        private Label lblStatus = null!;
        private Button btnCheck = null!;
        private Button btnDownload = null!;
        private Button btnClose = null!;

        private string? _downloadUrl;

        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

        // ---- DWM（ダークタイトルバー） ----
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
            ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public VersionForm(string currentVersion, string currentVersionNumeric)
        {
            _currentVersion = currentVersion;
            _currentVersionNumeric = currentVersionNumeric;
            BuildUI();
        }

        private void BuildUI()
        {
            bool dark = IsDarkMode();

            Color bg = dark ? Color.FromArgb(32, 32, 32) : Color.White;
            Color fg = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(20, 20, 20);
            Color btnBg = dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(240, 240, 240);
            Color border = dark ? Color.FromArgb(80, 80, 80) : Color.FromArgb(190, 190, 190);

            Text = "バージョン情報";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(400, 230);
            BackColor = bg;
            ForeColor = fg;
            Font = new Font("Yu Gothic UI", 9F);

            lblCurrent = new Label
            {
                Text = $"現在のバージョン: {_currentVersion}",
                AutoSize = true,
                Location = new Point(20, 20),
                ForeColor = fg
            };

            btnCheck = new Button
            {
                Text = "アップデートの確認",
                Location = new Point(20, 55),
                Size = new Size(160, 32),
                BackColor = btnBg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat
            };
            btnCheck.FlatAppearance.BorderColor = border;
            btnCheck.Click += async (_, _) => await CheckUpdateAsync();

            lblStatus = new Label
            {
                Text = "",
                Location = new Point(20, 100),
                Size = new Size(360, 40),
                ForeColor = fg
            };

            btnDownload = new Button
            {
                Text = "ダウンロード",
                Location = new Point(20, 150),
                Size = new Size(130, 32),
                BackColor = Color.FromArgb(0, 120, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Visible = false
            };
            btnDownload.FlatAppearance.BorderSize = 0;
            btnDownload.Click += (_, _) => OpenDownloadUrl();

            btnClose = new Button
            {
                Text = "閉じる",
                Location = new Point(300, 150),
                Size = new Size(80, 32),
                BackColor = btnBg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat
            };
            btnClose.FlatAppearance.BorderColor = border;
            btnClose.Click += (_, _) => Close();

            Controls.Add(lblCurrent);
            Controls.Add(btnCheck);
            Controls.Add(lblStatus);
            Controls.Add(btnDownload);
            Controls.Add(btnClose);

            AcceptButton = btnCheck;
            CancelButton = btnClose;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (IsDarkMode())
            {
                int one = 1;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref one, sizeof(int));
            }
        }

        private async System.Threading.Tasks.Task CheckUpdateAsync()
        {
            btnCheck.Enabled = false;
            btnDownload.Visible = false;
            lblStatus.Text = "確認中...";

            try
            {
                string json = await http.GetStringAsync(API_URL);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool status = root.TryGetProperty("status", out var s)
                              && s.ValueKind == JsonValueKind.True;

                string? latest = root.TryGetProperty("data", out var d)
                                 ? d.GetString()
                                 : null;

                if (!status || string.IsNullOrWhiteSpace(latest))
                {
                    lblStatus.Text = "アップデート情報を取得できませんでした。";
                    return;
                }

                if (IsNewerVersion(_currentVersionNumeric, latest))
                {
                    lblStatus.Text = $"新しいバージョン {latest} が利用可能です。";
                    _downloadUrl = $"{DOWNLOAD_BASE}/{latest}/1620kHz-Windows-App.exe";
                    btnDownload.Visible = true;
                }
                else
                {
                    lblStatus.Text = "最新バージョンです。";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "通信エラー: " + ex.Message;
            }
            finally
            {
                btnCheck.Enabled = true;
            }
        }

        private void OpenDownloadUrl()
        {
            if (string.IsNullOrEmpty(_downloadUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _downloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "ブラウザを開けませんでした: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsNewerVersion(string current, string latest)
        {
            static Version Parse(string v)
            {
                var m = Regex.Match(v, @"^\d+(\.\d+)*");
                return m.Success ? Version.Parse(m.Value) : new Version(0, 0);
            }
            return Parse(latest) > Parse(current);
        }

        private static bool IsDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0; // 0 = dark
            }
            catch { return false; }
        }
    }
}