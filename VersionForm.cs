using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
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
        private string? _latestVersion;

        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };

        // ---- DWM（ダークタイトルバー） ----
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
            ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        static VersionForm()
        {
            // Shift-JIS (CP932) を .NET Core で使えるようにする
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

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
                Text = "更新する",
                Location = new Point(20, 150),
                Size = new Size(130, 32),
                BackColor = Color.FromArgb(0, 120, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Visible = false
            };
            btnDownload.FlatAppearance.BorderSize = 0;
            btnDownload.Click += async (_, _) => await DownloadAndInstallAsync();

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
                    _latestVersion = latest;
                    lblStatus.Text = $"新しいバージョン {latest} が利用可能です。";
                    _downloadUrl = $"{DOWNLOAD_BASE}/{latest}/1620kHz-Windows-App.exe";
                    btnDownload.Visible = true;
                }
                else
                {
                    _latestVersion = null;
                    lblStatus.Text = "最新バージョンです。";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "通信エラー: " + ex.Message;
            }
            finally
            {
                if (!IsDisposed) btnCheck.Enabled = true;
            }
        }

        /// <summary>
        /// 新しい exe を %TEMP% にダウンロードし、Form1 に更新適用を依頼する。
        /// 実際のファイル差し替えは Form1.RequestUpdateAndRestart が担当。
        /// </summary>
        private async System.Threading.Tasks.Task DownloadAndInstallAsync()
        {
            if (string.IsNullOrEmpty(_downloadUrl) || string.IsNullOrEmpty(_latestVersion))
                return;

            var confirm = MessageBox.Show(this,
                $"バージョン {_latestVersion} に更新します。\n\n" +
                "更新後、アプリは自動的に再起動します。\n" +
                "よろしいですか？",
                "アップデートの確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            btnCheck.Enabled = false;
            btnDownload.Enabled = false;
            btnClose.Enabled = false;
            lblStatus.Text = "ダウンロード中...";

            string tempDir = Path.Combine(Path.GetTempPath(), "HighwayRadioUpdate");
            string newExe = Path.Combine(tempDir, "1620kHz-Windows-App.exe");

            try
            {
                // 一時フォルダ準備
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { /* 続行 */ }
                }
                Directory.CreateDirectory(tempDir);

                // ダウンロード（進捗付き）
                using (var response = await http.GetAsync(_downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long? total = response.Content.Headers.ContentLength;

                    using var src = await response.Content.ReadAsStreamAsync();
                    using var dst = File.Create(newExe);

                    var buffer = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await src.ReadAsync(buffer.AsMemory())) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, n));
                        read += n;

                        if (IsDisposed) return;

                        if (total.HasValue && total.Value > 0)
                        {
                            int pct = (int)(read * 100 / total.Value);
                            lblStatus.Text = $"ダウンロード中... {pct}%";
                        }
                        else
                        {
                            lblStatus.Text = $"ダウンロード中... {read / 1024 / 1024} MB";
                        }
                    }
                }

                if (IsDisposed) return;

                lblStatus.Text = "更新を適用しています...";

                // Form1 に更新適用を依頼
                var owner = Owner as Form1;
                if (owner == null)
                {
                    lblStatus.Text = "更新の適用に失敗しました（内部エラー）。";
                    MessageBox.Show(this,
                        "更新の適用に失敗しました。\n" +
                        "（Form1 への参照を取得できませんでした）",
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnCheck.Enabled = true;
                    btnDownload.Enabled = true;
                    btnClose.Enabled = true;
                    return;
                }

                bool ok = owner.RequestUpdateAndRestart(newExe);

                if (ok)
                {
                    // モーダルを閉じる。後続は ShowVersionPopup 側で処理される。
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    // RequestUpdateAndRestart 側で MessageBox 済み
                    btnCheck.Enabled = true;
                    btnDownload.Enabled = true;
                    btnClose.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                lblStatus.Text = "更新に失敗しました: " + ex.Message;
                MessageBox.Show(this,
                    "更新に失敗しました。\n\n" + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

                btnCheck.Enabled = true;
                btnDownload.Enabled = true;
                btnClose.Enabled = true;
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