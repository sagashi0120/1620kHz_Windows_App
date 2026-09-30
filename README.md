# 1620kHz_Windows_App

「[ハイウェイラジオ 情報まとめ](https://highwayradio.cloudfree.jp/)」を Windows デスクトップアプリとして使うためのラッパーアプリです。

## 概要

WebView2 を利用してサイトを表示し、ブラウザでは実現できない以下の機能を追加しています。

- **グローバルホットキー**（`Ctrl + Shift + H`）でどこからでも呼び出し
- **タスクトレイ常駐**（×ボタンで閉じても常駐し、すぐ呼び出せる）
- **Windows 起動時の自動起動**（サイトの「設定」から ON/OFF）
- **設定の保存**（ショートカットキー・自動起動の設定を次回起動時にも引き継ぎ）
- **アプリバージョン確認**（サイトから最新版をチェック、ダウンロードリンクを表示）
- **Windows のライト/ダークモードに追従**
- **User-Agent に `HighwayRadioApp/x.x.x` を付加**（サーバー側でアプリからのアクセスを判別可能）

## 動作環境

| 項目 | 要件 |
|---|---|
| OS | Windows 10 (バージョン 1803 以降) / Windows 11 |
| ランタイム | .NET 10 デスクトップランタイム |
| その他 | Microsoft Edge WebView2 Runtime |

> **補足**
> - Windows 11、および更新済みの Windows 10 には **WebView2 Runtime が標準搭載**されています。
> - WebView2 が見つからない場合は、起動時にダウンロードページを案内します。

## 使い方

### 起動

`1620kHz-Windows-App.exe` をダブルクリックすると、サイトが表示されます。

### ホットキー

| キー | 動作 |
|---|---|
| `Ctrl + Shift + H` | どこからでもアプリを最前面に呼び出し |
| `Alt + ←` | 前のページへ戻る |
| `Alt + →` | 次のページへ進む |
| `F5` | 再読み込み |

> ホットキーはサイトの「設定」→「ショートカットキー」から有効化できます。
> 他のアプリと競合して登録に失敗した場合は、通知でお知らせします。

### 設定

サイトの「設定」から以下を切り替えられます。設定は保存され、次回起動時にも引き継がれます。

| 設定 | 内容 |
|---|---|
| ショートカットキー | `Ctrl + Shift + H` の有効／無効 |
| Windows 起動時に自動起動 | サインイン時に自動で起動（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` に登録） |

### タスクトレイ

- ×ボタンで閉じると、タスクトレイに常駐します（プロセスは終了しません）
- タスクトレイのアイコンをダブルクリック、または `Ctrl + Shift + H` で復帰
- 完全に終了するには、タスクトレイアイコンを右クリック → 「終了」

### バージョン確認

サイトの設定画面から「アプリバージョンを確認」をクリックすると、最新版の有無を確認できます。

## アンインストール

1. タスクトレイアイコンを右クリック → 「終了」
2. 解凍したフォルダを削除
3. 以下のフォルダにユーザーデータが保存されているので、必要に応じて削除
   ```
   %LOCALAPPDATA%\1620kHz_Windows_App
   ```
4. 「Windows 起動時に自動起動」を ON にしていた場合は、レジストリの登録を削除
   ```
   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
   （値の名前: 1620kHz-Windows-App）
   ```

## 開発

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

### リリース

[release.ps1](release.ps1) を使うと、バージョン更新から GitHub Release の作成までを 1 コマンドで実行できます。

```powershell
.\release.ps1 0.4.0            # VERSION 更新 → publish → commit/tag/push → Release 作成
.\release.ps1 0.4.0 -DryRun    # ビルドと検証のみ（コミット・タグ・push はしない）
```

## ライセンス

[LICENSE](LICENSE) を参照してください。

## リンク

- [ハイウェイラジオ 情報まとめ](https://highwayradio.cloudfree.jp/)
- [不具合報告・要望](https://github.com/sagashi0120/1620kHz_Windows_App/issues)
