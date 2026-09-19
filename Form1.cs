using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.Runtime.InteropServices;

namespace _1620kHz_Windows_App
{
    public partial class Form1 : Form
    {
        private readonly WebView2 webView = new();

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
        private const uint MOD_NOREPEAT = 0x4000;

        private const int SW_RESTORE = 9;

        //（Ctrl+Alt+H）
        private const uint HOTKEY_MODIFIERS = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        private const uint HOTKEY_VK = 0x48; // 'H'

        // 状態管理
        private bool _shortcutEnabled = false;
        private bool _hotkeyRegistered = false;

        // バージョン
        private const string VERSION = "0.1.0+--WebViewNative";

        public Form1()
        {
            InitializeComponent();

            Text = "ハイウェイラジオ 情報まとめ";
            Width = 1200;
            Height = 800;

            // ---- WebView2 ----
            webView.Dock = DockStyle.Fill;
            Controls.Add(webView);

            // アプリがアクティブなときのショートカット
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

            Load += async (_, _) =>
            {
                await webView.EnsureCoreWebView2Async();

                webView.CoreWebView2.DocumentTitleChanged += (s, e) => Text = webView.CoreWebView2.DocumentTitle;

                // ページ遷移後、現在のショートカット状態をページへ反映
                webView.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    await ApplyShortcutStateAsync();
                };

                // ページ → アプリ
                webView.CoreWebView2.WebMessageReceived += async (s, e) =>
                {
                    string source = e.Source;
                    if (!source.StartsWith("https://highwayradio.cloudfree.jp/"))
                        return;

                    string message = e.TryGetWebMessageAsString();
                    System.Diagnostics.Debug.WriteLine("MSG FROM WEB: " + message);

                    switch (message)
                    {
                        case "enableShortcut":
                            bool ok = EnableShortcut();
                            await ApplyShortcutStateAsync();
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
                    }
                };

                // 初期状態：shortcut: false
                string js = $@"
                    window.APP_INFO_WEBVIEW = Object.freeze({{
                        version: '{VERSION}',
                        shortcut: false
                    }});
                ";
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(js);
                webView.CoreWebView2.Navigate("https://highwayradio.cloudfree.jp/");
            };
        }

        // ---- ページに現在のショートカット状態を反映 ----
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
        }

        // ---- ホットキー登録 ----
        private bool EnableShortcut()
        {
            if (_hotkeyRegistered)
            {
                _shortcutEnabled = true;
                return true;
            }

            if (RegisterHotKey(Handle, HOTKEY_ID, HOTKEY_MODIFIERS, HOTKEY_VK))
            {
                _hotkeyRegistered = true;
                _shortcutEnabled = true;
                return true;
            }

            // 他アプリと衝突などで失敗
            _shortcutEnabled = false;
            return false;
        }

        // ---- ホットキー解除 ----
        private void DisableShortcut()
        {
            if (_hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                _hotkeyRegistered = false;
            }
            _shortcutEnabled = false;
        }

        // ---- 標準タイトルバー色 ----
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            int captionColor = ToColorRef(ColorTranslator.FromHtml("#006432"));
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            int textColor = ToColorRef(Color.White);
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

            // ここではホットキー登録しない（デフォルトは false）
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            DisableShortcut();
            base.OnHandleDestroyed(e);
        }

        // ---- ホットキー受信 ----
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ActivateFromHotkey();
                return;
            }
            base.WndProc(ref m);
        }

        private void ActivateFromHotkey()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized)
                ShowWindow(Handle, SW_RESTORE);

            Activate();
            SetForegroundWindow(Handle);
            BringToFront();
        }

        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
    }
}