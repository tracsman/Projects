<#
.SYNOPSIS
    Installs the GHCAdmin GitHub Copilot Administrative Helper to your OneDrive Commercial folder.

.DESCRIPTION
    Designed to be run directly from GitHub (e.g. via iex). Interactively prompts for
    configuration, checks prerequisites (VS Code, Git), installs missing tools, then
    deploys the GHCAdmin files to OneDrive Commercial and initializes a Git repository.

.EXAMPLE
    irm https://aka.ms/GHCAdmin | iex
#>

# ── Helpers ───────────────────────────────────────────────────────────────────

function Refresh-Path {
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")
}

function Find-Exe {
    param([string]$Name, [string[]]$FallbackPaths)
    if (Get-Command $Name -ErrorAction SilentlyContinue) { return $true }
    foreach ($p in $FallbackPaths) { if (Test-Path $p) { return $true } }
    return $false
}

function Install-FromUrl {
    param([string]$Label, [string]$Url, [string[]]$Args)
    $installer = Join-Path $env:TEMP "$Label`_install.exe"
    Write-Host "`n>> Installing $Label..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri $Url -OutFile $installer -UseBasicParsing
        Start-Process -FilePath $installer -ArgumentList $Args -Wait
        Remove-Item $installer -ErrorAction SilentlyContinue
        Refresh-Path
        return $true
    }
    catch {
        Write-Host "   [X]  Failed to install ${Label}: $_" -ForegroundColor Red
        return $false
    }
}

# ── Main ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GHCAdmin - GitHub Copilot Admin Helper"      -ForegroundColor Cyan
Write-Host "  Installation Script"                          -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# -- VS Code --
Write-Host "`n>> Checking for Visual Studio Code..." -ForegroundColor Cyan
$vscodePaths = @("$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe", "$env:ProgramFiles\Microsoft VS Code\Code.exe")
if (Find-Exe "code" $vscodePaths) {
    Write-Host "   [OK] Visual Studio Code is installed." -ForegroundColor Green
} else {
    Write-Host "   [!]  Visual Studio Code was not found." -ForegroundColor Yellow
    $r = Read-Host "   Install now? (Y/n)"
    if ($r -match '^(y|yes)?$') {
        $ok = Install-FromUrl "VSCode" "https://update.code.visualstudio.com/latest/win32-x64-user/stable" @("/verysilent", "/mergetasks=!runcode,addtopath")
        if (-not $ok -or -not (Find-Exe "code" $vscodePaths)) {
            Write-Host "   [X]  VS Code installation failed. Install manually and re-run." -ForegroundColor Red; return
        }
    } else {
        Write-Host "   [X]  VS Code is required." -ForegroundColor Red; return
    }
}

# -- Git --
Write-Host "`n>> Checking for Git..." -ForegroundColor Cyan
$gitPaths = @("$env:ProgramFiles\Git\cmd\git.exe", "${env:ProgramFiles(x86)}\Git\cmd\git.exe")
if (Find-Exe "git" $gitPaths) {
    Write-Host "   [OK] Git is installed." -ForegroundColor Green
} else {
    Write-Host "   [!]  Git was not found." -ForegroundColor Yellow
    $r = Read-Host "   Install now? (Y/n)"
    if ($r -match '^(y|yes)?$') {
        # Resolve latest 64-bit installer URL from GitHub
        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/git-for-windows/git/releases/latest" -UseBasicParsing
            $gitUrl  = ($release.assets | Where-Object { $_.name -match "Git-.*-64-bit\.exe$" } | Select-Object -First 1).browser_download_url
        } catch {
            Write-Host "   [X]  Could not resolve latest Git release: $_" -ForegroundColor Red; return
        }
        $ok = Install-FromUrl "Git" $gitUrl @("/VERYSILENT", "/NORESTART")
        if (-not $ok -or -not (Find-Exe "git" $gitPaths)) {
            Write-Host "   [X]  Git installation failed. Install manually and re-run." -ForegroundColor Red; return
        }
    } else {
        Write-Host "   [X]  Git is required." -ForegroundColor Red; return
    }
}

# -- OneDrive Commercial --
Write-Host "`n>> Checking for OneDrive Commercial..." -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($env:OneDriveCommercial) -or -not (Test-Path $env:OneDriveCommercial)) {
    Write-Host "   [X]  OneDrive for Work is not available. Sign in and re-run." -ForegroundColor Red; return
}
Write-Host "   [OK] $env:OneDriveCommercial" -ForegroundColor Green

# ── Target folder ─────────────────────────────────────────────────────────────

$defaultFolder = "Documents\Copilot\ToDo"
Write-Host "`n   Repo location inside OneDrive Commercial:" -ForegroundColor White
$folderInput = Read-Host "   Subfolder path (default: $defaultFolder)"
if ([string]::IsNullOrWhiteSpace($folderInput)) { $folderInput = $defaultFolder }
$TargetPath = Join-Path $env:OneDriveCommercial $folderInput

# ── Desktop shortcut (runs early so it's created even if overwrite is declined) ─

Write-Host "`n>> Creating desktop shortcut..." -ForegroundColor Cyan
$codePath = (Get-Command code -ErrorAction SilentlyContinue).Source
if (-not $codePath) {
    foreach ($c in $vscodePaths) { if (Test-Path $c) { $codePath = $c; break } }
}
if ($codePath) {
    try {
        $shell    = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "GHCAdmin ToDo.lnk"))
        $shortcut.TargetPath       = $codePath
        $shortcut.Arguments        = "`"$TargetPath`""
        $shortcut.WorkingDirectory = $TargetPath
        $shortcut.Description      = "Open GHCAdmin ToDo in VS Code"
        $shortcut.Save()
        Write-Host "   [OK] Desktop shortcut created." -ForegroundColor Green
    } catch {
        Write-Host "   [!]  Could not create shortcut: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "   [!]  Could not locate VS Code — skipping shortcut." -ForegroundColor Yellow
}

if (Test-Path $TargetPath) {
    Write-Host "   [!]  Folder already exists: $TargetPath" -ForegroundColor Yellow
    Write-Host "   [!!] WARNING: Continuing will REPLACE all files in this folder," -ForegroundColor Red
    Write-Host "   [!!] including your TODO.md. Any tasks you have added will be" -ForegroundColor Red
    Write-Host "   [!!] PERMANENTLY DELETED and replaced with a blank template." -ForegroundColor Red
    Write-Host "   [!!] A backup of TODO.md will be created, but other files" -ForegroundColor Red
    Write-Host "   [!!] (README.md, copilot-instructions.md) will be overwritten." -ForegroundColor Red
    if ((Read-Host "   Type 'yes' to confirm overwrite (y/N)") -notmatch '^(y|yes)$') {
        Write-Host "   [X]  Installation cancelled." -ForegroundColor Red; return
    }
} else {
    if ((Read-Host "   Create folder '$TargetPath'? (Y/n)") -match '^(y|yes)?$') {
        try { New-Item -Path $TargetPath -ItemType Directory -Force | Out-Null }
        catch { Write-Host "   [X]  Failed to create folder: $_" -ForegroundColor Red; return }
        Write-Host "   [OK] Folder created." -ForegroundColor Green
    } else {
        Write-Host "   [X]  Installation cancelled." -ForegroundColor Red; return
    }
}

# ── Git init ──────────────────────────────────────────────────────────────────

Write-Host "`n>> Initializing Git repository..." -ForegroundColor Cyan
if (Test-Path (Join-Path $TargetPath ".git")) {
    Write-Host "   [OK] Already a Git repo — skipping init." -ForegroundColor Green
} else {
    Push-Location $TargetPath
    git init | Out-Null
    Pop-Location
    if ($LASTEXITCODE -ne 0) { Write-Host "   [X]  git init failed." -ForegroundColor Red; return }
    Write-Host "   [OK] Repository initialized." -ForegroundColor Green
}

# ── Download files ────────────────────────────────────────────────────────────

$repoOwner = "tracsman"
$repoName  = "Projects"
$branch    = "main"
$baseUrl   = "https://raw.githubusercontent.com/$repoOwner/$repoName/$branch/GHCAdmin/GHCAdmin"

$files = @("README.md", "TODO.md", ".github/copilot-instructions.md")

# ── Backup TODO.md before overwriting ─────────────────────────────────────────

$todoFile = Join-Path $TargetPath "TODO.md"
if (Test-Path $todoFile) {
    Write-Host "`n>> Backing up existing TODO.md..." -ForegroundColor Cyan
    $bakPath = Join-Path $TargetPath "TODO.md.bak"
    if (Test-Path $bakPath) {
        $i = 2
        while (Test-Path (Join-Path $TargetPath "TODO.md.bak$i")) { $i++ }
        $bakPath = Join-Path $TargetPath "TODO.md.bak$i"
    }
    try {
        Copy-Item -Path $todoFile -Destination $bakPath -Force
        Write-Host "   [OK] Backup saved to $(Split-Path $bakPath -Leaf)" -ForegroundColor Green
    } catch {
        Write-Host "   [X]  Failed to back up TODO.md: $_" -ForegroundColor Red; return
    }
}

Write-Host "`n>> Downloading files from GitHub..." -ForegroundColor Cyan
$allOk = $true
foreach ($file in $files) {
    $dest    = Join-Path $TargetPath $file
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
    try {
        Invoke-WebRequest -Uri "$baseUrl/$file" -OutFile $dest -UseBasicParsing
        Write-Host "   [OK] $file" -ForegroundColor Green
    } catch {
        Write-Host "   [X]  $file — $_" -ForegroundColor Red
        $allOk = $false
    }
}
if (-not $allOk) { Write-Host "   [!]  Some downloads failed." -ForegroundColor Yellow }

# ── Update copilot-instructions.md path if non-default folder ─────────────────

$instructionsFile = Join-Path $TargetPath ".github\copilot-instructions.md"
if ((Test-Path $instructionsFile) -and ($folderInput -ne $defaultFolder)) {
    $defaultTodoPath = '$env:OneDriveCommercial\Documents\Copilot\ToDo\TODO.md'
    $actualTodoPath  = '$env:OneDriveCommercial\' + $folderInput + '\TODO.md'
    (Get-Content $instructionsFile -Raw).Replace($defaultTodoPath, $actualTodoPath) |
        Set-Content -Path $instructionsFile -NoNewline
    Write-Host "   [OK] Updated TODO path in copilot-instructions.md" -ForegroundColor Green
}

# ── Initial commit ────────────────────────────────────────────────────────────

Write-Host "`n>> Creating initial commit..." -ForegroundColor Cyan
Push-Location $TargetPath
git add -A 2>&1 | Out-Null
git commit -m "Initial GHCAdmin setup" 2>&1 | Out-Null
Pop-Location
if ($LASTEXITCODE -eq 0) {
    Write-Host "   [OK] Initial commit created." -ForegroundColor Green
} else {
    Write-Host "   [!]  Commit skipped — files may already be committed." -ForegroundColor Yellow
}

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "  Installation complete!"                      -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Open in VS Code:  code `"$TargetPath`""     -ForegroundColor Gray
Write-Host "  Or double-click 'GHCAdmin ToDo' on your Desktop." -ForegroundColor Gray
Write-Host ""
