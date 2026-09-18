$ErrorActionPreference = "Stop"
$RepoName = "RoiLingo"
$Visibility = "public"
$Description = "Windows ROI live OCR translator with Tesseract, WebView translators, official APIs, and LibreTranslate/Argos support."
$Topics = @("windows", "wpf", "csharp", "ocr", "tesseract", "translation", "webview2", "roblox", "libretranslate", "argos-translate", "i18n", "overlay")
$Tag = "v2.0.0"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

function Step([string]$Text) { Write-Host "[CHECK] $Text" -ForegroundColor Cyan }
function Ok([string]$Text) { Write-Host "[OK] $Text" -ForegroundColor Green }
function Warn([string]$Text) { Write-Host "[WARN] $Text" -ForegroundColor Yellow }
function Fail([string]$Text, [string]$Recovery) {
    Write-Host "[ERROR] $Text" -ForegroundColor Red
    if ($Recovery) { Write-Host "[RECOVERY] $Recovery" -ForegroundColor Yellow }
    exit 1
}

function Test-NativeSuccess([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = "SilentlyContinue"
        & $Command *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Refresh-Path {
    $machine = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $user = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machine;$user;$env:ProgramFiles\Git\cmd;$env:ProgramFiles\GitHub CLI;$env:ProgramFiles\dotnet"
}

function Ensure-WingetPackage([string]$Command, [string]$PackageId, [string]$FriendlyName) {
    if (Get-Command $Command -ErrorAction SilentlyContinue) { Ok "$FriendlyName found"; return }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Fail "$FriendlyName is missing and winget is unavailable." "Install $FriendlyName manually, then rerun github-bootstrap.cmd"
    }
    Warn "$FriendlyName is missing. Installing with winget..."
    winget install --id $PackageId -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { Fail "$FriendlyName installation failed." "winget install --id $PackageId -e" }
    Refresh-Path
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        Fail "$FriendlyName was installed but is not visible in this terminal." "Close this window and run github-bootstrap.cmd again."
    }
    Ok "$FriendlyName installed"
}

Step "Prerequisites"
Ensure-WingetPackage "git" "Git.Git" "Git"
Ensure-WingetPackage "dotnet" "Microsoft.DotNet.SDK.8" ".NET SDK"
Ensure-WingetPackage "gh" "GitHub.cli" "GitHub CLI"

$dotnetVersion = (& dotnet --version).Trim()
if ([Version](($dotnetVersion -split '-', 2)[0]) -lt [Version]"8.0.0") {
    Warn ".NET SDK 8+ is required. Installing .NET 8 SDK..."
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { Fail ".NET 8 SDK installation failed." "winget install --id Microsoft.DotNet.SDK.8 -e" }
    Refresh-Path
    $dotnetVersion = (& dotnet --version).Trim()
    if ([Version](($dotnetVersion -split '-', 2)[0]) -lt [Version]"8.0.0") {
        Fail ".NET SDK 8+ is installed but not selected in this terminal." "Close this window and run github-bootstrap.cmd again."
    }
}
Ok ".NET SDK $dotnetVersion"

Step "GitHub authentication"
if (-not (Test-NativeSuccess { gh auth status })) {
    Warn "GitHub login is required. A browser login flow will open."
    gh auth login --web --git-protocol https
    if ($LASTEXITCODE -ne 0) { Fail "GitHub login failed." "gh auth login --web --git-protocol https" }
}
$Owner = (gh api user --jq .login).Trim()
if (-not $Owner) { Fail "Could not read GitHub account name." "gh auth status" }
$FullRepo = "$Owner/$RepoName"
Ok "GitHub account: $Owner"

Step "Git identity"
if (-not (git config --global user.name)) { git config --global user.name $Owner }
if (-not (git config --global user.email)) { git config --global user.email "$Owner@users.noreply.github.com" }
Ok "Git identity configured"

Step "Build verification"
try {
    & (Join-Path $PSScriptRoot "verify.ps1")
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Fail "Build verification failed." ".\scripts\verify.cmd"
}
if ($LASTEXITCODE -ne 0) { Fail "Build verification failed." ".\scripts\verify.cmd" }
Ok "Release/x64 build passed"

Step "Local repository"
if (-not (Test-Path ".git")) { git init | Out-Host }
git branch -M main
git add -A
$hasChanges = $false
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { $hasChanges = $true }
if ($hasChanges) {
    # Do not probe a brand-new repository with `git rev-parse --verify HEAD` while
    # ErrorActionPreference=Stop. On Windows PowerShell the expected stderr from a
    # missing HEAD is promoted to NativeCommandError and aborts the whole publisher.
    $gitDir = (git rev-parse --git-dir).Trim()
    $mainRef = Join-Path $gitDir "refs\heads\main"
    $packedRefs = Join-Path $gitDir "packed-refs"
    $hasHead = Test-Path $mainRef
    if (-not $hasHead -and (Test-Path $packedRefs)) {
        $hasHead = Select-String -Path $packedRefs -Pattern " refs/heads/main$" -Quiet
    }
    if ($hasHead) { git commit -m "feat: background capture, editable ROI, fast startup and i18n" | Out-Host }
    else { git commit -m "feat: initialize RoiLingo" | Out-Host }
    if ($LASTEXITCODE -ne 0) { Fail "Git commit failed." "git status; git add -A; git commit -m 'feat: initialize RoiLingo'" }
}
Ok "Local Git repository ready"

Step "GitHub repository $FullRepo"
if (-not (Test-NativeSuccess { gh repo view $FullRepo })) {
    $visibilityFlag = if ($Visibility -eq "private") { "--private" } else { "--public" }
    gh repo create $FullRepo $visibilityFlag --description $Description
    if ($LASTEXITCODE -ne 0) { Fail "Repository creation failed." "gh repo create $FullRepo --public --description `"$Description`"" }
    Ok "Repository created"
} else {
    Ok "Repository already exists"
}

$remoteNames = @(git remote)
if ($remoteNames -notcontains "origin") {
    git remote add origin "https://github.com/$FullRepo.git"
} else {
    $origin = (git remote get-url origin).Trim()
    if ($origin -notmatch [regex]::Escape($FullRepo)) { git remote set-url origin "https://github.com/$FullRepo.git" }
}

$aboutUpdated = Test-NativeSuccess { gh repo edit $FullRepo --description $Description }
$topicFailures = @()
foreach ($topic in $Topics) {
    if (-not (Test-NativeSuccess { gh repo edit $FullRepo --add-topic $topic })) { $topicFailures += $topic }
}
if ($aboutUpdated -and $topicFailures.Count -eq 0) {
    Ok "Repository About/Topics updated"
} else {
    $detail = if ($topicFailures.Count -gt 0) { " Topics not applied: " + ($topicFailures -join ", ") } else { "" }
    Warn ("Repository About/Topics update was partial. This does not block source publishing." + $detail)
}

Step "Synchronize existing remote"
$remoteMainExists = Test-NativeSuccess { git ls-remote --exit-code --heads origin main }
if ($remoteMainExists) {
    git fetch origin main --prune | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "Could not fetch origin/main." "git fetch origin main --prune" }

    $localSha = (git rev-parse main).Trim()
    $remoteSha = (git rev-parse origin/main).Trim()
    if ($localSha -ne $remoteSha) {
        # Previous bootstrap attempts may already have created RoiLingo on GitHub from a different
        # local folder, producing unrelated root commits. Do not force-push or delete remote history.
        # Rebase the current complete working tree onto origin/main as one normal sync commit.
        Warn "Remote main already contains history. Rebasing this release snapshot on top of origin/main..."
        git reset --soft origin/main
        if ($LASTEXITCODE -ne 0) { Fail "Could not align local history with origin/main." "git fetch origin main; git reset --soft origin/main" }
        git add -A
        git diff --cached --quiet
        if ($LASTEXITCODE -ne 0) {
            git commit -m "chore: sync RoiLingo $Tag" | Out-Host
            if ($LASTEXITCODE -ne 0) { Fail "Sync commit failed." "git status; git add -A; git commit -m 'chore: sync RoiLingo $Tag'" }
        }
        Ok "Local release snapshot is based on existing origin/main"
    } else {
        Ok "Local main already matches origin/main"
    }
} else {
    Ok "Remote main does not exist yet"
}

Step "Push main"
git push -u origin main
if ($LASTEXITCODE -ne 0) { Fail "Push failed." "git fetch origin main --prune; git status; git log --oneline --decorate -5; git push -u origin main" }
Ok "main pushed"

Step "GitHub Actions"
$runId = ""
for ($attempt = 1; $attempt -le 8 -and -not $runId; $attempt++) {
    Start-Sleep -Seconds 4
    $runId = (gh run list --repo $FullRepo --workflow windows-build.yml --branch main --limit 1 --json databaseId --jq '.[0].databaseId' 2>$null).Trim()
}
if ($runId) {
    gh run watch $runId --repo $FullRepo --exit-status
    if ($LASTEXITCODE -ne 0) { Fail "GitHub Actions build failed." "gh run view $runId --repo $FullRepo --log-failed" }
    Ok "GitHub Actions build passed"
} else {
    Warn "Workflow run was not visible after ~30 seconds. Push succeeded; use: gh run list --repo $FullRepo"
}

Step "Release package"
& (Join-Path $PSScriptRoot "build_release.ps1")
if ($LASTEXITCODE -ne 0) { Fail "Release package build failed." ".\scripts\build_release.ps1" }

Step "Tag and Release $Tag"
if (-not (Test-NativeSuccess { git ls-remote --exit-code --tags origin "refs/tags/$Tag" })) {
    if (-not (Test-NativeSuccess { git show-ref --verify --quiet "refs/tags/$Tag" })) {
        git tag -a $Tag -m "RoiLingo $Tag"
        if ($LASTEXITCODE -ne 0) { Fail "Local tag creation failed." "git tag -a $Tag -m 'RoiLingo $Tag'" }
    }
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { Fail "Tag push failed." "git push origin $Tag" }
}
$asset = Join-Path $root "publish\RoiLingo-win-x64.zip"
if (-not (Test-NativeSuccess { gh release view $Tag --repo $FullRepo })) {
    gh release create $Tag --repo $FullRepo --title "RoiLingo $Tag" --generate-notes
    if ($LASTEXITCODE -ne 0) { Fail "GitHub Release creation failed." "gh release create $Tag --repo $FullRepo --generate-notes" }
}
gh release upload $Tag $asset --repo $FullRepo --clobber
if ($LASTEXITCODE -ne 0) { Fail "Release asset upload failed." "gh release upload $Tag `"$asset`" --repo $FullRepo --clobber" }

$repoUrl = (gh repo view $FullRepo --json url --jq .url).Trim()
$releaseUrl = (gh release view $Tag --repo $FullRepo --json url --jq .url).Trim()
Write-Host ""
Ok "Publish complete"
Write-Host "Repository: $repoUrl"
Write-Host "Release:    $releaseUrl"
Write-Host "About:      $Description"
