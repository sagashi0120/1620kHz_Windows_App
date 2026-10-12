# 1620kHz Windows App

[ハイウェイラジオ 情報まとめ](https://highwayradio.cloudfree.jp/) を Windows のデスクトップアプリとして使うためのラッパーアプリです。WebView2 でサイトを表示し、ブラウザだけでは実現できない機能を追加しています。

- ダウンロード: [Releases](https://github.com/sagashi0120/1620kHz_Windows_App/releases)
- 不具合報告・要望: [Issues](https://github.com/sagashi0120/1620kHz_Windows_App/issues)

## 主な機能

| 機能 | 内容 |
|---|---|
| グローバルホットキー | `Ctrl + Shift + H` でどこからでも呼び出し |
| タスクトレイ常駐 | ×ボタンで閉じても常駐し、すぐ呼び出せる |
| Windows 起動時の自動起動 | サイトの「設定」から ON/OFF |
| 設定の保存 | ショートカットキーの設定を次回起動時にも引き継ぎ |
| バージョン確認・自動更新 | サイトから最新版を確認し、そのまま更新して再起動 |
| ライト/ダークモード追従 | Windows の設定に合わせて配色を変更 |
| User-Agent の付加 | `HighwayRadioApp/x.x.x` を付加し、サーバー側でアプリからのアクセスを判別可能 |
| 単一インスタンス | 二重起動すると既存のウィンドウを前面に表示 |
| ページ内ショートカット | `Alt + ←` / `Alt + →` / `F5` |

## 動作環境

| 項目 | 要件 |
|---|---|
| OS | Windows 10 バージョン 1709 以降 / Windows 11 |
| CPU | 64bit (x64)。32bit 版は配布していません |
| .NET | インストール不要（.NET 10 を同梱した自己完結型の単一 exe） |
| その他 | Microsoft Edge WebView2 Runtime |

> **補足**
> - Windows 11、および更新済みの Windows 10（1803 以降）には **WebView2 Runtime が標準で入っています**。
> - Windows 10 1709 では WebView2 Runtime の自動配信対象外のため、手動でのインストールが必要です。
> - WebView2 が見つからない場合は起動時にエラーダイアログが表示されます。その場合は [WebView2 Runtime のダウンロードページ](https://developer.microsoft.com/microsoft-edge/webview2/) からインストールしてください。

### 動作しない環境

- **Windows 7 / 8 / 8.1** — .NET 10 と WebView2 のどちらも対応していません（[Windows 7 について](#windows-7-について)）。
- **32bit Windows** — 配布している exe が x64 のみです。
- **Windows 10 1703 以前** — WebView2 が対応していません。
- **Windows Server Core** — WebView2 には Desktop Experience が必要です。

## 使い方

### ダウンロードと起動

1. [Releases](https://github.com/sagashi0120/1620kHz_Windows_App/releases) から `1620kHz-Windows-App.exe` をダウンロード
2. 書き込み可能な任意のフォルダに置く（デスクトップなど）
3. ダブルクリックで起動

インストーラーはありません。exe 単体で動作します（.NET 10 と `WebView2Loader.dll` を同梱）。

> 自動更新は exe を直接置き換えるため、`C:\Program Files` など書き込み権限のない場所に置くと更新に失敗します。

### ホットキー

| キー | 動作 | 条件 |
|---|---|---|
| `Ctrl + Shift + H` | どこからでもアプリを最前面に呼び出し | サイトの「設定」→「ショートカットキー」で有効化した場合のみ |
| `Alt + ←` | 前のページへ戻る | ウィンドウがアクティブなとき |
| `Alt + →` | 次のページへ進む | 同上 |
| `F5` | 再読み込み | 同上 |

> 他のアプリと競合して登録に失敗した場合は、通知（バルーン）でお知らせします。

### 設定

サイトの「設定」から以下を切り替えられます。

| 設定 | 内容 | 保存先 |
|---|---|---|
| ショートカットキー | `Ctrl + Shift + H` の有効／無効 | `%LOCALAPPDATA%\1620kHz_Windows_App\settings.json` |
| Windows 起動時に自動起動 | サインイン時に自動で起動 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`（値の名前: `1620kHz-Windows-App`） |

設定は保存され、次回起動時にも引き継がれます。exe を移動した場合は、自動起動の登録パスも起動時に自動で更新されます。

### タスクトレイ

- ×ボタンで閉じると、タスクトレイに常駐します（プロセスは終了しません）
- タスクトレイのアイコンをダブルクリック、または `Ctrl + Shift + H` で復帰
- 完全に終了するには、タスクトレイアイコンを右クリック → 「終了」

### バージョン確認と自動更新

サイトの設定画面から「アプリバージョンを確認」をクリックすると、最新版の有無を確認できます。

1. 新しいバージョンがある場合は「更新する」をクリック
2. 新しい exe を `%TEMP%\HighwayRadioUpdate` にダウンロード
3. 確認ダイアログの後、アプリが終了 → exe を差し替え → 自動で再起動

> 差し替えには exe への書き込み権限が必要です。ウイルス対策ソフトが更新用バッチ（`cmd.exe`）をブロックすると失敗することがあります。その場合は [Releases](https://github.com/sagashi0120/1620kHz_Windows_App/releases) から手動でダウンロードして置き換えてください。

## データの保存場所

| 内容 | パス |
|---|---|
| 設定 | `%LOCALAPPDATA%\1620kHz_Windows_App\settings.json` |
| WebView2 のユーザーデータ（キャッシュ等） | `%LOCALAPPDATA%\1620kHz_Windows_App` |
| デバッグログ | `%TEMP%\1620khz_debug.log`（起動のたびに上書き） |

## アンインストール

1. タスクトレイアイコンを右クリック → 「終了」
2. 解凍したフォルダ（exe）を削除
3. ユーザーデータが不要なら `%LOCALAPPDATA%\1620kHz_Windows_App` を削除
4. 「Windows 起動時に自動起動」を ON にしていた場合は、レジストリの登録を削除

   ```
   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
   （値の名前: 1620kHz-Windows-App）
   ```

## トラブルシューティング

| 症状 | 対処 |
|---|---|
| 起動時に「WebView2 の初期化に失敗しました」と出る | [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) をインストールする |
| ホットキーが効かない | 他アプリと競合しています。サイトの設定で一度 OFF → ON にする |
| 更新に失敗する | exe の置き場所が書き込み不可、またはウイルス対策のブロック。手動で最新 exe を上書きする |
| ウィンドウが出てこない | タスクトレイのアイコンをダブルクリック。二重起動時は既存のウィンドウが表示されます |
| 原因を調べたい | `%TEMP%\1620khz_debug.log` を確認する |

## 開発

### 必要なもの

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- リリース作業を行う場合: `git` / [GitHub CLI (`gh`)](https://cli.github.com/)（認証済みであること）

### ビルド

```powershell
dotnet build -c Release
```

### 配布用パッケージ作成

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

出力先：

```
bin\Release\net10.0-windows\win-x64\publish\
```

> `WebView2Loader.dll`（WebView2 がランタイムを探すためのネイティブ DLL）は、csproj の `IncludeNativeLibrariesForSelfExtract` によって exe に同梱されます。
> この設定を外すと publish フォルダに併置されるだけになり、exe 単体で配布した環境で `WebView2Loader.dll` が見つからず起動に失敗します。

### リリース

[release.ps1](release.ps1) を使うと、バージョン更新から GitHub Release の作成までを 1 コマンドで実行できます。

```powershell
.\release.ps1 0.4.0            # VERSION 更新 → publish → commit/tag/push → Release 作成
.\release.ps1 0.4.0 -DryRun    # ビルドと検証のみ（コミット・タグ・push はしない）
```

現在のバージョンは [Form1.cs](Form1.cs) の `VERSION` 定数で管理されており、`release.ps1` が自動で書き換えます。

### サイトとの連携

ページ側から `window.chrome.webview.postMessage(...)` で以下のコマンドを送ると、アプリ側が処理します。

| メッセージ | 動作 |
|---|---|
| `enableShortcut` / `disableShortcut` / `queryShortcutState` | グローバルホットキーの有効化・無効化・状態取得 |
| `enableAutoStart` / `disableAutoStart` | 自動起動の ON / OFF |
| `showPopupVersion` | バージョン情報ダイアログを表示 |
| `applyUpdate:<新しい exe のフルパス>` | 指定された exe に差し替えて再起動 |

アプリ側はページに `window.APP_INFO_WEBVIEW`（`version` / `shortcut` / `autoStart`）を注入し、状態が変わると `appinfochange` イベントを発火します。なお、送信元が `https://highwayradio.cloudfree.jp/` 以外のメッセージは無視されます。

- 更新確認 API: `GET https://highwayradio.cloudfree.jp/api/web-app/version` → `{ "status": true, "data": "0.4.2" }`
- User-Agent: 通常の User-Agent に ` HighwayRadioApp/<バージョン>` を付加

## Windows 7 について

Windows 7（および 8 / 8.1）では動作しません。追加のランタイムを入れれば動くというものでもありません。

- アプリは **.NET 10** 製ですが、.NET 6 以降は Windows 7 に対応しておらず、Windows 7 用のランタイム自体が存在しません（[対応 OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)）。
- 画面表示に使っている **Microsoft Edge WebView2** も、2023 年 1 月のバージョン 109 を最後に Windows 7 のサポートを終了しています（[告知](https://blogs.windows.com/msedgedev/2022/12/09/microsoft-edge-and-webview2-ending-support-for-windows-7-and-windows-8-8-1/)）。本アプリが使用している SDK 1.0.4191.47 はこの対象外です。

Windows 7 でサイトを見たい場合は、お使いのブラウザで <https://highwayradio.cloudfree.jp/> を開いてください（タスクトレイ常駐やホットキーなどの付加機能は利用できません）。

## ライセンス

[MIT License](LICENSE)

## リンク

- [ハイウェイラジオ 情報まとめ](https://highwayradio.cloudfree.jp/)
- [不具合報告・要望](https://github.com/sagashi0120/1620kHz_Windows_App/issues)
