using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace _1620kHz_Windows_App
{
    public partial class Form1 : Form
    {
        private readonly WebView2 webView = new();
        private NotifyIcon? trayIcon;
        private ContextMenuStrip? trayMenu;

        // ---- 単一インスタンス制御 ----
        private static readonly Mutex InstanceMutex;
        private static readonly bool IsFirstInstance;
        private static readonly uint WM_SHOW_EXISTING;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        static Form1()
        {
            InstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\1620kHz_Windows_App_SingleInstance",
                createdNew: out bool createdNew);
            IsFirstInstance = createdNew;

            WM_SHOW_EXISTING = RegisterWindowMessage("1620kHz_Windows_App_ShowExisting");
        }

        // ---- デバッグログ（ファイル出力） ----
        private static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "1620khz_debug.log");

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        // ---- DWM ----
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        // ---- グローバルホットキー ----
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x0001;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int SW_RESTORE = 9;

        //（Ctrl+Shift+H）
        private const uint HOTKEY_MODIFIERS = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
        private const uint HOTKEY_VK = 0x48; // 'H'

        // 状態管理
        private bool _shortcutEnabled = false;
        private bool _hotkeyRegistered = false;
        private bool _userWantsShortcut = false;

        private bool _realExit = false;

        // 2つ目のインスタンスかどうか
        private readonly bool _isSecondInstance;

        // ActivateFromHotkey のデバウンス用
        private DateTime _lastActivateUtc = DateTime.MinValue;

        // バージョン
        private const string VERSION = "0.3.1+--WebViewNative";
        private const string PAGE_URL = "https://highwayradio.cloudfree.jp/"; // debug: https://localhost, release: https://highwayradio.cloudfree.jp/

        // バージョン文字列から数値部分だけを取り出す
        private static string NumericVersion
        {
            get
            {
                var m = Regex.Match(VERSION, @"^\d+(\.\d+)*");
                return m.Success ? m.Value : VERSION;
            }
        }

        public Form1()
        {
            InitializeComponent();

            // === 2つ目のインスタンス：既存インスタンスを前面化して静かに終了 ===
            if (!IsFirstInstance)
            {
                _isSecondInstance = true;
                _realExit = true;

                _ = Handle; // ハンドルを確実に作成（BeginInvoke のため）
                PostMessage(HWND_BROADCAST, WM_SHOW_EXISTING, IntPtr.Zero, IntPtr.Zero);

                // メッセージループ開始後に静かに閉じる
                BeginInvoke(new Action(Close));
                return;
            }

            try { File.WriteAllText(LogPath, ""); } catch { }
            Log("=== App Start ===");

            Text = "ハイウェイラジオ 情報まとめ";
            Width = 1200;
            Height = 800;

            webView.Dock = DockStyle.Fill;
            Controls.Add(webView);

            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (webView.CoreWebView2 == null) return;
                switch (e.KeyCode)
                {
                    case Keys.Left when e.Alt:
                        if (webView.CoreWebView2.CanGoBack) webView.CoreWebView2.GoBack();
                        e.Handled = true;
                        break;
                    case Keys.Right when e.Alt:
                        if (webView.CoreWebView2.CanGoForward) webView.CoreWebView2.GoForward();
                        e.Handled = true;
                        break;
                    case Keys.F5:
                        webView.CoreWebView2.Reload();
                        e.Handled = true;
                        break;
                }
            };

            SetupTrayIcon();

            Load += async (_, _) =>
            {
                try
                {
                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "1620kHz_Windows_App");
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        null, userDataFolder);
                    await webView.EnsureCoreWebView2Async(env);
                    Log("WebView2 initialized");

                    webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                        Text = webView.CoreWebView2.DocumentTitle;

                    webView.CoreWebView2.NavigationCompleted += async (s, e) =>
                    {
                        Log("NavigationCompleted: " + webView.CoreWebView2.Source);
                        await ApplyShortcutStateAsync();
                    };

                    webView.CoreWebView2.WebMessageReceived += async (s, e) =>
                    {
                        string? source = e.Source;
                        Log("MSG SOURCE: " + (source ?? "(null)"));

                        if (source == null ||
                            !source.StartsWith(PAGE_URL))
                            return;

                        string message;
                        try
                        {
                            message = e.TryGetWebMessageAsString();
                        }
                        catch
                        {
                            Log("MSG non-string, ignored");
                            return;
                        }

                        Log("MSG FROM WEB: " + message);

                        switch (message)
                        {
                            case "enableShortcut":
                                bool ok = EnableShortcut();
                                await ApplyShortcutStateAsync();
                                if (!ok)
                                {
                                    trayIcon?.ShowBalloonTip(
                                        3000,
                                        "ショートカット登録失敗",
                                        "Ctrl+Shift+H は他アプリと衝突しています。",
                                        ToolTipIcon.Warning);
                                }
                                webView.CoreWebView2.PostWebMessageAsString(
                                    ok ? "shortcut:true" : "shortcut:failed");
                                break;

                            case "disableShortcut":
                                DisableShortcut();
                                await ApplyShortcutStateAsync();
                                webView.CoreWebView2.PostWebMessageAsString("shortcut:false");
                                break;

                            case "queryShortcutState":
                                webView.CoreWebView2.PostWebMessageAsString(
                                    _shortcutEnabled ? "shortcut:true" : "shortcut:false");
                                break;

                            case "showPopupVersion":
                                Log("showPopupVersion received (deferring)");
                                BeginInvoke(new Action(() => ShowVersionPopup()));
                                break;
                        }
                    };

                    string js = $@"
                        window.APP_INFO_WEBVIEW = Object.freeze({{
                            version: '{VERSION}',
                            shortcut: false
                        }});
                    ";
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(js);
                    webView.CoreWebView2.Settings.UserAgent = webView.CoreWebView2.Settings.UserAgent + $" HighwayRadioApp/{VERSION}";
                    webView.CoreWebView2.Navigate(PAGE_URL);
                }
                catch (Exception ex)
                {
                    Log("WebView2 init FAILED: " + ex);
                    MessageBox.Show(
                        "WebView2 の初期化に失敗しました。\n" +
                        "Microsoft Edge WebView2 Runtime がインストールされているか確認してください。\n\n" +
                        ex.Message,
                        "起動エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };
        }

        // 2つ目のインスタンスは一切表示させない
        protected override void SetVisibleCore(bool value)
        {
            if (_isSecondInstance)
            {
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        // ---- バージョンポップアップ ----
        private void ShowVersionPopup()
        {
            try
            {
                if (!Visible) Show();
                ShowInTaskbar = true;

                using var dlg = new VersionForm(VERSION, NumericVersion);
                dlg.Icon = this.Icon;
                dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log("ShowVersionPopup EXCEPTION: " + ex);
                MessageBox.Show(this,
                    "バージョンポップアップでエラーが発生しました:\n\n" + ex,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("開く(&O)", null, (_, _) => ActivateFromHotkey());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("終了(&X)", null, (_, _) =>
            {
                _realExit = true;
                Close();
            });

            trayIcon = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = "ハイウェイラジオ 情報まとめ",
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            trayIcon.DoubleClick += (_, _) => ActivateFromHotkey();
        }

        private async System.Threading.Tasks.Task ApplyShortcutStateAsync()
        {
            if (webView.CoreWebView2 == null) return;

            string state = _shortcutEnabled ? "true" : "false";
            string js = $@"
                window.APP_INFO_WEBVIEW = Object.freeze({{
                    version: '{VERSION}',
                    shortcut: {state}
                }});
                window.dispatchEvent(new CustomEvent('appinfochange', {{
                    detail: {{ shortcut: {state} }}
                }}));
            ";
            await webView.CoreWebView2.ExecuteScriptAsync(js);
            Log("Applied state to page: shortcut=" + state);
        }

        private bool EnableShortcut()
        {
            _userWantsShortcut = true;

            if (_hotkeyRegistered)
            {
                _shortcutEnabled = true;
                return true;
            }

            if (RegisterHotKey(Handle, HOTKEY_ID, HOTKEY_MODIFIERS, HOTKEY_VK))
            {
                _hotkeyRegistered = true;
                _shortcutEnabled = true;
                Log($"RegisterHotKey OK: Handle={Handle}");
                return true;
            }

            _shortcutEnabled = false;
            Log("RegisterHotKey FAILED. Win32Error = " + Marshal.GetLastWin32Error());
            return false;
        }

        private void DisableShortcut()
        {
            _userWantsShortcut = false;

            if (_hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                _hotkeyRegistered = false;
            }
            _shortcutEnabled = false;
            Log("DisableShortcut called");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            int captionColor = ToColorRef(ColorTranslator.FromHtml("#006432"));
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            int textColor = ToColorRef(Color.White);
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

            Log($"OnHandleCreated: Handle={Handle}, userWants={_userWantsShortcut}");

            if (_userWantsShortcut)
            {
                _hotkeyRegistered = false;
                EnableShortcut();
                Log("Re-registered hotkey on new Handle");
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Log($"OnHandleDestroyed: Handle={Handle}");
            if (_hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                _hotkeyRegistered = false;
            }
            _shortcutEnabled = false;
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 2つ目のインスタンスはトレイもWebViewも初期化していない
            if (_isSecondInstance)
            {
                base.OnFormClosing(e);
                return;
            }

#if DEBUG
            if (!_realExit)
            {
                Log("OnFormClosing (DEBUG: exiting)");
                trayIcon!.Visible = false;
                trayIcon.Dispose();
                trayMenu?.Dispose();
                webView.Dispose();
                base.OnFormClosing(e);
                return;
            }
#endif

            if (!_realExit && e.CloseReason == CloseReason.UserClosing)
            {
                // ★ 静かにトレイへ
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }

            trayIcon!.Visible = false;
            trayIcon.Dispose();
            trayMenu?.Dispose();
            webView.Dispose();

            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            // 2つ目のインスタンスからの「前面に出て」要求
            if ((uint)m.Msg == WM_SHOW_EXISTING)
            {
                Log("WM_SHOW_EXISTING received");
                ActivateFromHotkey();
                return;
            }

            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                Log("WM_HOTKEY received");
                ActivateFromHotkey();
                return;
            }
            base.WndProc(ref m);
        }

        private void ActivateFromHotkey()
        {
            // 連続起動（二重に開いたように見える挙動）を防ぐデバウンス
            var now = DateTime.UtcNow;
            if ((now - _lastActivateUtc).TotalMilliseconds < 250)
            {
                Log("ActivateFromHotkey debounced");
                return;
            }
            _lastActivateUtc = now;

            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            if (!Visible)
                Show();

            ShowInTaskbar = true;

            // 既にアクティブなら余計な再アクティブ化をしない（ちらつき防止）
            if (Form.ActiveForm != this)
            {
                Activate();
                SetForegroundWindow(Handle);
            }
        }

        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
    }
}