using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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
        private static EventWaitHandle? ShowEvent;
        private static EventWaitHandle? UpdateExitEvent;

        private const string MutexName = @"Local\1620kHz_Windows_App_Mutex";
        private const string ShowEventName = @"Local\1620kHz_Windows_App_ShowEvent";
        private const string UpdateExitEventName = @"Local\1620kHz_Windows_App_UpdateExit";

        static Form1()
        {
            // Shift-JIS (CP932) を .NET Core で使えるようにする（バッチ書き出し用）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Mutex 取得（更新直後のクリーンアップラグ対策で最大 3 秒リトライ）
            InstanceMutex = new Mutex(false, MutexName);

            bool acquired = false;

            for (int i = 0; i < 30; i++)
            {
                try
                {
                    if (InstanceMutex.WaitOne(100, false))
                    {
                        acquired = true;
                        break;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 前回プロセスが異常終了 → 自分がオーナーになる
                    acquired = true;
                    break;
                }
            }

            IsFirstInstance = acquired;

            if (IsFirstInstance)
            {
                ShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                UpdateExitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, UpdateExitEventName);
            }
            else
            {
                // 既存インスタンスへ「表示して」とシグナル
                SignalEventWithRetry(ShowEventName);
            }
        }

        private static void SignalEventWithRetry(string eventName)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(eventName);
                    ev.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    return;
                }
            }
        }

        // ---- デバッグログ ----
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

        private const uint HOTKEY_MODIFIERS = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
        private const uint HOTKEY_VK = 0x48; // 'H'

        // 状態管理
        private bool _shortcutEnabled = false;
        private bool _hotkeyRegistered = false;
        private bool _userWantsShortcut = false;
        private volatile bool _realExit = false;
        private readonly bool _isSecondInstance;

        // デバウンス用
        private DateTime _lastActivateUtc = DateTime.MinValue;
        private Thread? _listenerThread;

        // 読み込み中オーバーレイ
        private bool _firstNavigationCompleted = false;
        private Label _loadingOverlay = null!;

        // バージョン
        private const string VERSION = "0.3.3+--WebViewNative";
        private const string PAGE_URL = "https://highwayradio.cloudfree.jp/";

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

            // === 2つ目のインスタンス：既存を起こして即終了 ===
            if (!IsFirstInstance)
            {
                _isSecondInstance = true;
                _realExit = true;
                Environment.Exit(0);
                return; // 到達しない
            }

            try { File.WriteAllText(LogPath, ""); } catch { }

            Log("=== App Start ===");
            Log("Version: " + VERSION);
            Log("ProcessPath: " + (Environment.ProcessPath ?? "(null)"));

            Text = "ハイウェイラジオ 情報まとめ";
            Width = 1200;
            Height = 800;

            BackColor = SystemColors.Window;

            // 動的に切り替えない（白窓/ハンドル再生成対策）
            ShowInTaskbar = true;

            webView.Dock = DockStyle.Fill;
            Controls.Add(webView);

            // WebView2 の初期化/初回読み込み中に真っ白が出ないよう前面を覆う
            _loadingOverlay = new Label
            {
                Dock = DockStyle.Fill,
                Text = "読み込み中...",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.GrayText,
                Font = new Font("Yu Gothic UI", 12F),
                Visible = true
            };

            Controls.Add(_loadingOverlay);
            _loadingOverlay.BringToFront();

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
                StartBackgroundListeners();

                try
                {
                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "1620kHz_Windows_App");

                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        null, userDataFolder);

                    await webView.EnsureCoreWebView2Async(env);
                    Log("WebView2 initialized");

                    if (webView.CoreWebView2 == null)
                        throw new InvalidOperationException("CoreWebView2 initialization returned null.");

                    webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                    {
                        try
                        {
                            if (!IsDisposed)
                            {
                                var title = webView.CoreWebView2?.DocumentTitle;
                                if (!string.IsNullOrEmpty(title))
                                    Text = title;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("DocumentTitleChanged EXCEPTION: " + ex);
                        }
                    };

                    webView.CoreWebView2.NavigationCompleted += async (s, e) =>
                    {
                        try
                        {
                            Log("NavigationCompleted: " + (webView.CoreWebView2?.Source ?? "(null)"));

                            if (!e.IsSuccess)
                                Log("Navigation failed: " + e.WebErrorStatus);

                            if (!_firstNavigationCompleted)
                            {
                                _firstNavigationCompleted = true;

                                if (!IsDisposed)
                                {
                                    _loadingOverlay.Visible = false;

                                    try { webView.Focus(); } catch { }
                                }
                            }

                            await ApplyShortcutStateAsync();
                        }
                        catch (Exception ex)
                        {
                            Log("NavigationCompleted EXCEPTION: " + ex);
                        }
                    };

                    webView.CoreWebView2.WebMessageReceived += async (s, e) =>
                    {
                        try
                        {
                            if (IsDisposed || _realExit)
                                return;

                            string? source = e.Source;
                            Log("MSG SOURCE: " + (source ?? "(null)"));

                            if (source == null || !source.StartsWith(PAGE_URL))
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

                                    if (!IsDisposed && !_realExit)
                                    {
                                        try
                                        {
                                            BeginInvoke(new Action(() =>
                                            {
                                                if (!IsDisposed && !_realExit)
                                                    ShowVersionPopup();
                                            }));
                                        }
                                        catch (Exception ex)
                                        {
                                            Log("showPopupVersion BeginInvoke EXCEPTION: " + ex);
                                        }
                                    }

                                    break;
                            }

                            // 自動アップデート用（"applyUpdate:<newExePath>"）
                            if (message.StartsWith("applyUpdate:", StringComparison.Ordinal))
                            {
                                string newExe = message.Substring("applyUpdate:".Length).Trim();
                                Log("applyUpdate received: " + newExe);

                                bool started = RequestUpdateAndRestart(newExe);

                                webView.CoreWebView2.PostWebMessageAsString(
                                    started ? "update:ok" : "update:failed");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("WebMessageReceived EXCEPTION: " + ex);
                        }
                    };

                    string js = $@"
                        window.APP_INFO_WEBVIEW = Object.freeze({{
                            version: '{VERSION}',
                            shortcut: false
                        }});
                    ";

                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(js);

                    webView.CoreWebView2.Settings.UserAgent =
                        webView.CoreWebView2.Settings.UserAgent + $" HighwayRadioApp/{VERSION}";

                    webView.CoreWebView2.Navigate(PAGE_URL);
                }
                catch (Exception ex)
                {
                    Log("WebView2 init FAILED: " + ex);

                    try
                    {
                        _loadingOverlay.Text = "WebView2 の初期化に失敗しました";
                    }
                    catch { }

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

        protected override void SetVisibleCore(bool value)
        {
            if (_isSecondInstance)
            {
                base.SetVisibleCore(false);
                return;
            }

            base.SetVisibleCore(value);
        }

        // ---- バックグラウンドリスナー ----
        private void StartBackgroundListeners()
        {
            if (_listenerThread != null) return;

            _listenerThread = new Thread(() =>
            {
                Log("BackgroundListeners started");

                var handles = new WaitHandle[] { ShowEvent!, UpdateExitEvent! };

                while (!_realExit)
                {
                    int idx;

                    try
                    {
                        idx = WaitHandle.WaitAny(handles, 500);
                    }
                    catch
                    {
                        break;
                    }

                    if (_realExit) break;

                    if (idx == 0)
                    {
                        Log("ShowEvent signaled");

                        try
                        {
                            BeginInvoke(new Action(ActivateFromHotkey));
                        }
                        catch (InvalidOperationException)
                        {
                            break;
                        }
                        catch
                        {
                        }
                    }
                    else if (idx == 1)
                    {
                        Log("UpdateExitEvent signaled");

                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                _realExit = true;
                                Close();
                            }));
                        }
                        catch { }

                        break;
                    }
                }

                Log("BackgroundListeners ended");
            })
            {
                IsBackground = true,
                Name = "BackgroundListeners"
            };

            _listenerThread.Start();
        }

        // ---- 自動アップデート ----
        /// <summary>
        /// ダウンロード済みの新 exe に差し替えて再起動する。
        /// 呼び出し後、このインスタンスは（呼び出し元のモーダルが閉じた後に）終了する。
        /// </summary>
        /// <param name="newExePath">新バージョンの exe フルパス（%TEMP% 等）</param>
        public bool RequestUpdateAndRestart(string newExePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newExePath) || !File.Exists(newExePath))
                {
                    Log("Update: new exe not found: " + newExePath);
                    return false;
                }

                string currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
                int pid = Environment.ProcessId;

                // 同一パスへの上書きは起動中は不可なので、別フォルダから渡す前提
                if (string.Equals(
                        Path.GetFullPath(newExePath),
                        Path.GetFullPath(currentExe),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Log("Update: new exe path equals current exe path");
                    return false;
                }

                Log($"Update: {newExePath} -> {currentExe} (pid={pid})");

                string batPath = Path.Combine(
                    Path.GetTempPath(), $"1620khz_update_{Guid.NewGuid():N}.bat");

                // PID の消滅待ち → コピー（リトライ） → 新 exe 起動 → バッチ自己削除
                string bat = $"""
@echo off
setlocal

set "OLD_PID={pid}"
set "NEW_EXE={newExePath}"
set "CUR_EXE={currentExe}"

:waitloop
tasklist /FI "PID eq %OLD_PID%" 2>NUL | find "%OLD_PID%" >NUL
if %errorlevel%==0 (
    timeout /t 1 /nobreak >NUL
    goto waitloop
)

set /a COPY_TRIES=0

:copyretry
copy /Y "%NEW_EXE%" "%CUR_EXE%" >NUL 2>&1
if %errorlevel% neq 0 (
    set /a COPY_TRIES+=1
    if %COPY_TRIES% GEQ 30 (
        echo Update failed: could not replace exe. > "%TEMP%\1620khz_update_error.txt"
        goto cleanup
    )
    timeout /t 1 /nobreak >NUL
    goto copyretry
)

start "" "%CUR_EXE%"

:cleanup
del "%~f0"
""";

                // 日本語パス対応で CP932 として書き出す
                File.WriteAllText(batPath, bat, Encoding.GetEncoding(932));

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batPath}\"\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                // フラグだけ立てる。実際の Close は呼び出し元（ShowVersionPopup）が
                // モーダルを閉じた後に BeginInvoke で行う。
                _realExit = true;

                return true;
            }
            catch (Exception ex)
            {
                Log("Update EXCEPTION: " + ex);

                MessageBox.Show(this,
                    "アップデートの適用に失敗しました:\n\n" + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }
        }

        // ---- バージョンポップアップ ----
        private void ShowVersionPopup()
        {
            if (IsDisposed || _realExit)
                return;

            try
            {
                if (!Visible)
                    Show();

                // ShowInTaskbar は動的に変えない（白窓/ハンドル再生成対策）

                using var dlg = new VersionForm(VERSION, NumericVersion);
                dlg.Icon = this.Icon;
                dlg.ShowDialog(this);

                // VersionForm 側で更新適用が予約された場合は自身を閉じる
                if (_realExit && !IsDisposed)
                {
                    Log("ShowVersionPopup: _realExit=true -> closing Form1");

                    try
                    {
                        BeginInvoke(new Action(Close));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log("ShowVersionPopup EXCEPTION: " + ex);

                if (!IsDisposed)
                {
                    MessageBox.Show(this,
                        "バージョンポップアップでエラーが発生しました:\n\n" + ex,
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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

        private async Task ApplyShortcutStateAsync()
        {
            try
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
            catch (Exception ex)
            {
                Log("ApplyShortcutStateAsync EXCEPTION: " + ex);
            }
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
            if (_isSecondInstance)
            {
                base.OnFormClosing(e);
                return;
            }

#if DEBUG
            if (!_realExit)
            {
                Log("OnFormClosing (DEBUG: exiting)");

                try { trayIcon!.Visible = false; } catch { }
                try { trayIcon?.Dispose(); } catch { }
                try { trayMenu?.Dispose(); } catch { }

                DisposeWebViewSafely();

                base.OnFormClosing(e);
                return;
            }
#endif

            if (!_realExit && e.CloseReason == CloseReason.UserClosing)
            {
                // ★ 静かにトレイへ
                // ※ ShowInTaskbar を動的に変えない（ハンドル再作成/白窓対策）
                e.Cancel = true;
                Hide();

                return;
            }

            // 完全終了時のクリーンアップ
            _realExit = true;

            try { trayIcon!.Visible = false; } catch { }
            try { trayIcon?.Dispose(); } catch { }
            try { trayMenu?.Dispose(); } catch { }

            DisposeWebViewSafely();

            try
            {
                ShowEvent?.Dispose();
                ShowEvent = null;
            }
            catch { }

            try
            {
                UpdateExitEvent?.Dispose();
                UpdateExitEvent = null;
            }
            catch { }

            try { InstanceMutex.ReleaseMutex(); } catch { }

            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
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
            if (IsDisposed || _realExit)
                return;

            // デバウンス（連続シグナルのちらつき防止）
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

            // ※ ShowInTaskbar = true; はしない（ハンドル再作成/白窓対策）

            if (Form.ActiveForm != this)
            {
                Activate();
                SetForegroundWindow(Handle);
            }
        }

        private void DisposeWebViewSafely()
        {
            try
            {
                webView.CoreWebView2?.Stop();
            }
            catch { }

            try
            {
                webView.Hide();
            }
            catch { }

            try
            {
                webView.Dispose();
            }
            catch { }
        }

        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
    }
}