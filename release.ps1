<#
.SYNOPSIS
1620kHz-Windows-App のリリース作業を 1 コマンドで実行します。

.DESCRIPTION
  1. Form1.cs の VERSION 定数を更新（"+" 以降のサフィックスは維持）
  2. dotnet publish（win-x64 / self-contained / single-file）
  3. 生成された exe を検証（ファイル名 / サイズ / 更新時刻 / SHA256）
  4. 追跡ファイルの変更をコミットし、軽量タグを作成して main とタグを push
  5. GitHub Release を下書きで作成 → exe を添付 → 公開（Pre-release）

.PARAMETER Version
  リリースするバージョン。既存タグと同じく数字のみの x.y.z 形式（例: 0.3.4）。

.PARAMETER Notes
  リリースノート本文。省略時は前回タグとの Full Changelog リンクを自動生成します。

.PARAMETER DryRun
  ビルドと検証のみ行い、コミット / タグ / push / Release 作成は行いません
  （Form1.cs は元に戻します）。

.PARAMETER Force
  続行確認のプロンプトを省略します。

.EXAMPLE
  .\release.ps1 0.3.4

.EXAMPLE
  .\release.ps1 0.3.4 -DryRun

.NOTES
  実行ポリシーで止められる場合は次のように実行してください:
    powershell -ExecutionPolicy Bypass -File .\release.ps1 0.3.4
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [string]$Notes = "",

    [switch]$DryRun,

    [switch]$Force
)

$ErrorActionPreference = "Continue"

# ---- 設定 ----
$RepoRoot     = $PSScriptRoot
if (-not $RepoRoot) { $RepoRoot = (Get-Location).Path }
$ExeName      = "1620kHz-Windows-App.exe"
$PublishDir   = Join-Path $RepoRoot "bin\Release\net10.0-windows\win-x64\publish"
$ExePath      = Join-Path $PublishDir $ExeName
$Form1Path    = Join-Path $RepoRoot "Form1.cs"
$MinExeSizeMB = 50
$IsPrerelease = $true
$Lf           = [Environment]::NewLine

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Info([string]$Message) {
    Write-Host "    $Message"
}

function Fail([string]$Message) {
    Write-Host ""
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    exit 1
}

function Run-Git([string[]]$GitArgs) {
    $out = & git @GitArgs 2>&1
    return (($out | ForEach-Object { $_.ToString() }) -join $Lf)
}

function Assert-Git([string[]]$GitArgs) {
    $text = Run-Git $GitArgs
    if ($LASTEXITCODE -ne 0) {
        Fail ("git " + ($GitArgs -join " ") + " に失敗しました。" + $Lf + $text)
    }
    return $text
}

$committed    = $false
$originalText = $null
$encoding     = $null

Push-Location $RepoRoot
try {
    # ---------------------------------------------------------------
    Write-Step "前提の確認"
    # ---------------------------------------------------------------
    foreach ($cmd in @("git", "gh", "dotnet")) {
        if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
            Fail "$cmd が見つかりません。PATH を確認してください。"
        }
    }
    & gh auth status > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Fail "gh CLI が未認証です。先に 'gh auth login' を実行してください。"
    }
    Write-Info "git / gh / dotnet すべて利用可能、gh 認証済み"

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Fail "バージョンは数字のみの x.y.z 形式で指定してください（例: 0.3.4）。"
    }
    $tagName = $Version

    $branch = (Assert-Git @("rev-parse", "--abbrev-ref", "HEAD")).Trim()
    if ($branch -ne "main") {
        Fail "main ブランチで実行してください（現在: $branch）。"
    }

    # ---------------------------------------------------------------
    Write-Step "リモートの最新状態を取得"
    # ---------------------------------------------------------------
    Assert-Git @("fetch", "origin", "--tags", "--prune") | Out-Null

    & git rev-parse -q --verify "refs/tags/$tagName" > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Fail "タグ $tagName はローカルに既に存在します。"
    }
    $remoteTag = & git ls-remote --tags origin "refs/tags/$tagName" 2>$null
    if ($remoteTag) {
        Fail "タグ $tagName は GitHub に既に存在します。"
    }
    Write-Info "タグ $tagName は未使用"

    # ---------------------------------------------------------------
    Write-Step "Form1.cs の VERSION 定数を更新"
    # ---------------------------------------------------------------
    $originalBytes = [System.IO.File]::ReadAllBytes($Form1Path)
    $hasBom = ($originalBytes.Length -ge 3 -and $originalBytes[0] -eq 0xEF -and $originalBytes[1] -eq 0xBB -and $originalBytes[2] -eq 0xBF)
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    $originalText = [System.IO.File]::ReadAllText($Form1Path, $encoding)

    $rx = [regex]'const\s+string\s+VERSION\s*=\s*"([^"]*)"'
    $m = $rx.Match($originalText)
    if (-not $m.Success) {
        Fail "Form1.cs に VERSION 定数が見つかりませんでした。"
    }
    $oldValue = $m.Groups[1].Value
    $suffix = ""
    $plusIndex = $oldValue.IndexOf("+")
    if ($plusIndex -ge 0) { $suffix = $oldValue.Substring($plusIndex) }
    $newValue = "$Version$suffix"
    if ($oldValue -eq $newValue) {
        Fail "VERSION は既に $newValue です。別のバージョンを指定してください。"
    }
    $newText = $originalText.Replace($m.Groups[0].Value, 'const string VERSION = "' + $newValue + '"')
    [System.IO.File]::WriteAllText($Form1Path, $newText, $encoding)
    Write-Info "VERSION: $oldValue  ->  $newValue"

    # ---------------------------------------------------------------
    Write-Step "コミット対象の確認"
    # ---------------------------------------------------------------
    $trackedChanges = (Assert-Git @("status", "--porcelain", "-uno")).Trim()
    $untrackedFiles = (Assert-Git @("ls-files", "--others", "--exclude-standard")).Trim()
    if ($trackedChanges) {
        Write-Host "追跡ファイルの変更（コミットされます）:"
        $trackedChanges -split "\r?\n" | ForEach-Object { Write-Host "    $_" }
    } else {
        Write-Info "追跡ファイルに変更はありません"
    }
    if ($untrackedFiles) {
        Write-Host ""
        Write-Host "未追跡ファイル（コミットされません）:"
        $untrackedFiles -split "\r?\n" | ForEach-Object { Write-Host "    $_" }
    }

    # ---------------------------------------------------------------
    Write-Step "dotnet publish（win-x64 / self-contained / single-file）"
    # ---------------------------------------------------------------
    $publishStart = Get-Date
    & dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --nologo
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet publish に失敗しました。"
    }

    # ---------------------------------------------------------------
    Write-Step "生成された exe を検証"
    # ---------------------------------------------------------------
    if (-not (Test-Path -LiteralPath $ExePath)) {
        Fail "exe が見つかりません: $ExePath"
    }
    $exe = Get-Item -LiteralPath $ExePath
    $exeNameActual = $exe.Name
    if ($exeNameActual -ne $ExeName) {
        Fail "アセット名が想定と異なります: $exeNameActual"
    }
    if ($exe.LastWriteTime -lt $publishStart.AddSeconds(-5)) {
        Fail "exe が今回のビルドで更新されていません（古い成果物の可能性があります）。"
    }
    $sizeMB = [math]::Round($exe.Length / 1MB, 1)
    if ($exe.Length -lt ($MinExeSizeMB * 1MB)) {
        Fail "exe が小さすぎます（$sizeMB MB）。自己完結型でビルドされていない可能性があります。"
    }
    $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExePath).Hash
    Write-Info "パス   : $ExePath"
    Write-Info ("サイズ : {0:N0} bytes ({1} MB)" -f $exe.Length, $sizeMB)
    Write-Info "SHA256 : $sha"

    # リリースノート
    if (-not $Notes) {
        $repoSlug = (& gh repo view --json nameWithOwner -q .nameWithOwner 2>$null)
        if (-not $repoSlug) { $repoSlug = "sagashi0120/1620kHz_Windows_App" }
        $repoSlug = $repoSlug.Trim()
        $prevTag = ""
        $prev = & git describe --tags --abbrev=0 HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $prev) { $prevTag = $prev.Trim() }
        if ($prevTag) {
            $Notes = "**Full Changelog**: https://github.com/$repoSlug/compare/$prevTag...$tagName"
        }
    }

    # ---------------------------------------------------------------
    if ($DryRun) {
        Write-Host ""
        Write-Host "----- DryRun: ここから先は実行しません -----" -ForegroundColor Yellow
        Write-Info "commit : $tagName"
        Write-Info "tag    : $tagName（軽量タグ）"
        Write-Info "push   : origin main / origin $tagName"
        Write-Info "release: $tagName（Pre-release）にアセット $ExeName を添付"
        if ($Notes) { Write-Info "notes  : $Notes" }
        Write-Host ""
        return
    }

    # ---------------------------------------------------------------
    if (-not $Force) {
        Write-Host ""
        Write-Host "上記の内容でコミット・タグ作成・push・Release 作成を行います。" -ForegroundColor Yellow
        $answer = Read-Host "続行しますか? (y/N)"
        if ($answer -notmatch '^(y|Y|yes|YES|Yes)$') {
            Write-Host "中止しました。"
            return
        }
    }

    # ---------------------------------------------------------------
    Write-Step "コミットとタグ作成"
    # ---------------------------------------------------------------
    Assert-Git @("add", "-u") | Out-Null
    Assert-Git @("commit", "-m", $tagName) | Out-Null
    $committed = $true
    Assert-Git @("tag", $tagName) | Out-Null
    Write-Info "commit / tag 完了"

    # ---------------------------------------------------------------
    Write-Step "push"
    # ---------------------------------------------------------------
    & git push origin main
    if ($LASTEXITCODE -ne 0) { Fail "git push origin main に失敗しました。" }
    & git push origin $tagName
    if ($LASTEXITCODE -ne 0) { Fail "git push origin $tagName に失敗しました。" }
    Write-Info "push 完了"

    # ---------------------------------------------------------------
    Write-Step "GitHub Release を作成（下書き → アセット添付 → 公開）"
    # ---------------------------------------------------------------
    $notesFile = Join-Path $env:TEMP ("1620khz-release-notes-" + $tagName + ".md")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($notesFile, $Notes, $utf8NoBom)

    try {
        $createArgs = @("release", "create", $tagName, "--title", $tagName, "--draft", "--notes-file", $notesFile)
        if ($IsPrerelease) { $createArgs += "--prerelease" }
        & gh @createArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create に失敗しました。" }

        & gh release upload $tagName $ExePath --clobber
        if ($LASTEXITCODE -ne 0) { throw "gh release upload に失敗しました。" }

        & gh release edit $tagName --draft=false
        if ($LASTEXITCODE -ne 0) { throw "gh release edit --draft=false に失敗しました。" }
    } catch {
        $msg = $_.Exception.Message
        Write-Host ""
        Write-Host "[WARN] $msg" -ForegroundColor Yellow
        Write-Host "コミットとタグ $tagName は push 済みです。リリース作成だけをやり直すには:" -ForegroundColor Yellow
        Write-Host "  gh release delete $tagName --yes"
        Write-Host ("  gh release create " + $tagName + " --title " + $tagName + " --prerelease --notes-file " + '"' + $notesFile + '"')
        Write-Host ("  gh release upload " + $tagName + " " + '"' + $ExePath + '"')
        Write-Host ("  gh release edit " + $tagName + " --draft=false")
        exit 1
    } finally {
        Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue
    }

    # ---------------------------------------------------------------
    Write-Step "完了確認"
    # ---------------------------------------------------------------
    $json = (& gh release view $tagName --json tagName,isDraft,isPrerelease,url,assets | Out-String)
    $rel = $json | ConvertFrom-Json
    Write-Info ("URL      : " + $rel.url)
    Write-Info ("Pre      : " + $rel.isPrerelease)
    foreach ($a in $rel.assets) {
        Write-Info ("Asset    : " + $a.name + "  (" + ("{0:N0}" -f $a.size) + " bytes)")
        if ($a.digest) {
            $expected = "sha256:" + $sha.ToLower()
            if ($a.digest -eq $expected) {
                Write-Info "Digest   : ローカルと一致"
            } else {
                Write-Info ("Digest   : " + $a.digest + "  (ローカルと不一致!)")
            }
        }
    }
    Write-Host ""
    Write-Host "リリース $tagName が完了しました。" -ForegroundColor Green
} finally {
    if (-not $committed -and $null -ne $originalText) {
        [System.IO.File]::WriteAllText($Form1Path, $originalText, $encoding)
        Write-Host ""
        Write-Host "Form1.cs を元に戻しました（作業ツリーは変更前の状態）。" -ForegroundColor Yellow
    }
    Pop-Location
}
