<#
.SYNOPSIS
    Installs the GHCAdmin GitHub Copilot Administrative Helper to your OneDrive Commercial folder.

.DESCRIPTION
    Designed to be run directly from GitHub (e.g. via iex). Interactively prompts for
    configuration, checks prerequisites (VS Code, Git), installs missing tools, then
    deploys the GHCAdmin files to OneDrive Commercial and initializes a Git repository.

.EXAMPLE
    irm https://raw.githubusercontent.com/<owner>/GHCAdmin/main/Install.ps1 | iex
#>

# ── Helper functions ──────────────────────────────────────────────────────────

function Write-Step {
    param([string]$Message)
    Write-Host "`n>> $Message" -ForegroundColor Cyan
}

function Write-OK {
    param([string]$Message)
    Write-Host "   [OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "   [!]  $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "   [X]  $Message" -ForegroundColor Red
}

# ── Prerequisite checks ──────────────────────────────────────────────────────

function Test-VSCode {
    # Check common locations and PATH
    if (Get-Command code -ErrorAction SilentlyContinue) { return $true }

    $paths = @(
        "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe",
        "$env:ProgramFiles\Microsoft VS Code\Code.exe"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $true }
    }
    return $false
}

function Install-VSCode {
    Write-Step "Installing Visual Studio Code..."
    $installer = Join-Path $env:TEMP "vscode_install.exe"
    try {
        Invoke-WebRequest -Uri "https://update.code.visualstudio.com/latest/win32-x64-user/stable" `
                          -OutFile $installer -UseBasicParsing
        Start-Process -FilePath $installer -ArgumentList "/verysilent", "/mergetasks=!runcode,addtopath" -Wait
        Remove-Item $installer -ErrorAction SilentlyContinue
        # Refresh PATH for the current session
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                     [System.Environment]::GetEnvironmentVariable("Path", "User")
        if (Test-VSCode) {
            Write-OK "Visual Studio Code installed successfully."
        } else {
            Write-Warn "Installer finished but VS Code was not detected on PATH. You may need to restart your shell."
        }
    }
    catch {
        Write-Err "Failed to download or run the VS Code installer: $_"
        return $false
    }
    return $true
}

function Test-Git {
    if (Get-Command git -ErrorAction SilentlyContinue) { return $true }

    $paths = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "${env:ProgramFiles(x86)}\Git\cmd\git.exe"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $true }
    }
    return $false
}

function Install-Git {
    Write-Step "Installing Git for Windows..."

    # Query the GitHub API once for the latest release
    $releaseUrl = "https://api.github.com/repos/git-for-windows/git/releases/latest"
    try {
        $release = Invoke-RestMethod -Uri $releaseUrl -UseBasicParsing
        $asset   = $release.assets | Where-Object { $_.name -match "Git-.*-64-bit\.exe$" } | Select-Object -First 1
        if (-not $asset) {
            Write-Err "Could not find a 64-bit installer asset in the latest Git release."
            return $false
        }
        $downloadUrl = $asset.browser_download_url
    }
    catch {
        Write-Err "Failed to query the latest Git release: $_"
        return $false
    }

    $installer = Join-Path $env:TEMP "git_install.exe"
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $installer -UseBasicParsing
        Start-Process -FilePath $installer -ArgumentList "/VERYSILENT", "/NORESTART" -Wait
        Remove-Item $installer -ErrorAction SilentlyContinue
        # Refresh PATH for the current session
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                     [System.Environment]::GetEnvironmentVariable("Path", "User")
        if (Test-Git) {
            Write-OK "Git for Windows installed successfully."
        } else {
            Write-Warn "Installer finished but git was not detected on PATH. You may need to restart your shell."
        }
    }
    catch {
        Write-Err "Failed to download or run the Git installer: $_"
        return $false
    }
    return $true
}

# ── Main ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  GHCAdmin — GitHub Copilot Admin Helper"      -ForegroundColor Cyan
Write-Host "  Installation Script"                          -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# -- Check VS Code --
Write-Step "Checking for Visual Studio Code..."
if (Test-VSCode) {
    Write-OK "Visual Studio Code is installed."
} else {
    Write-Warn "Visual Studio Code was not found."
    $response = Read-Host "   Would you like to install Visual Studio Code now? (Y/n)"
    if ($response -match '^(y|yes)?$') {
        if (-not (Install-VSCode)) {
            Write-Err "VS Code installation failed. Please install manually and re-run this script."
            return
        }
    } else {
        Write-Err "VS Code is required. Please install it and re-run this script."
        return
    }
}

# -- Check Git --
Write-Step "Checking for Git..."
if (Test-Git) {
    Write-OK "Git is installed."
} else {
    Write-Warn "Git was not found."
    $response = Read-Host "   Would you like to install Git for Windows now? (Y/n)"
    if ($response -match '^(y|yes)?$') {
        if (-not (Install-Git)) {
            Write-Err "Git installation failed. Please install manually and re-run this script."
            return
        }
    } else {
        Write-Err "Git is required. Please install it and re-run this script."
        return
    }
}

# -- Check OneDrive Commercial --
Write-Step "Checking for OneDrive Commercial (OneDrive - Microsoft)..."
if ([string]::IsNullOrWhiteSpace($env:OneDriveCommercial)) {
    Write-Err "The environment variable `$env:OneDriveCommercial is not set."
    Write-Err "OneDrive for Work (OneDrive - Microsoft) must be installed and running."
    Write-Err "Please sign in to OneDrive with your work account and re-run this script."
    return
}
if (-not (Test-Path $env:OneDriveCommercial)) {
    Write-Err "OneDriveCommercial points to '$env:OneDriveCommercial' but that path does not exist."
    Write-Err "Please ensure OneDrive is syncing and re-run this script."
    return
}
Write-OK "OneDrive Commercial is available at: $env:OneDriveCommercial"

Write-Step "Prerequisites verified."

# ── Prompt for target folder ──────────────────────────────────────────────────

$defaultFolder = "Documents\Copilot\ToDo"
Write-Host ""
Write-Host "   The GHCAdmin repo will be created inside your OneDrive Commercial folder:" -ForegroundColor White
Write-Host "   $env:OneDriveCommercial" -ForegroundColor Gray
Write-Host ""
$folderInput = Read-Host "   Enter a subfolder path (default: $defaultFolder)"
if ([string]::IsNullOrWhiteSpace($folderInput)) {
    $folderInput = $defaultFolder
}

$TargetPath = Join-Path $env:OneDriveCommercial $folderInput

if (Test-Path $TargetPath) {
    Write-Warn "Target folder already exists: $TargetPath"
    $overwrite = Read-Host "   Continue and overwrite existing files? (y/N)"
    if ($overwrite -notmatch '^(y|yes)$') {
        Write-Err "Installation cancelled."
        return
    }
} else {
    Write-Host "   Folder does not exist: $TargetPath" -ForegroundColor Gray
    $create = Read-Host "   Create this folder? (Y/n)"
    if ($create -match '^(y|yes)?$') {
        try {
            New-Item -Path $TargetPath -ItemType Directory -Force | Out-Null
            Write-OK "Created folder: $TargetPath"
        }
        catch {
            Write-Err "Failed to create folder: $_"
            return
        }
    } else {
        Write-Err "Installation cancelled — target folder is required."
        return
    }
}

Write-OK "Target path set to: $TargetPath"

# ── Initialize Git repo ──────────────────────────────────────────────────────

Write-Step "Initializing Git repository..."
Push-Location $TargetPath
try {
    if (Test-Path (Join-Path $TargetPath ".git")) {
        Write-OK "Git repository already exists in this folder — skipping init."
    } else {
        git init | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Err "git init failed."
            Pop-Location
            return
        }
        Write-OK "Git repository initialized."
    }
}
catch {
    Write-Err "Failed to initialize Git repository: $_"
    Pop-Location
    return
}
Pop-Location

# ── Download GHCAdmin files ──────────────────────────────────────────────────

# Update this to match your GitHub repository
$repoOwner = "YourGitHubUser"
$repoName  = "GHCAdmin"
$branch    = "main"
$baseUrl   = "https://raw.githubusercontent.com/$repoOwner/$repoName/$branch/GHCAdmin"

# Files to download (relative to the GHCAdmin folder in the repo)
$files = @(
    "README.md",
    "TODO.md",
    ".github/copilot-instructions.md"
)

Write-Step "Downloading GHCAdmin files from GitHub..."

$allSucceeded = $true
foreach ($file in $files) {
    $url        = "$baseUrl/$file"
    $destFile   = Join-Path $TargetPath $file
    $destDir    = Split-Path $destFile -Parent

    # Ensure subdirectories exist
    if (-not (Test-Path $destDir)) {
        New-Item -Path $destDir -ItemType Directory -Force | Out-Null
    }

    try {
        Invoke-WebRequest -Uri $url -OutFile $destFile -UseBasicParsing
        Write-OK "Downloaded: $file"
    }
    catch {
        Write-Err "Failed to download $file — $_"
        $allSucceeded = $false
    }
}

if (-not $allSucceeded) {
    Write-Warn "Some files could not be downloaded. Check the errors above."
} else {
    Write-OK "All files downloaded successfully."
}

# ── Update copilot-instructions.md with actual target path ────────────────────

$instructionsFile = Join-Path $TargetPath ".github\copilot-instructions.md"
if (Test-Path $instructionsFile) {
    $defaultTodoPath = '$env:OneDriveCommercial\Documents\Copilot\ToDo\TODO.md'
    $actualTodoPath  = '$env:OneDriveCommercial\' + $folderInput + '\TODO.md'

    if ($defaultTodoPath -ne $actualTodoPath) {
        Write-Step "Updating TODO.md path in copilot-instructions.md..."
        $content = Get-Content $instructionsFile -Raw
        $content = $content.Replace($defaultTodoPath, $actualTodoPath)
        Set-Content -Path $instructionsFile -Value $content -NoNewline
        Write-OK "Path updated to: $actualTodoPath"
    }
}

# ── Initial commit ────────────────────────────────────────────────────────────

Write-Step "Creating initial Git commit..."
Push-Location $TargetPath
try {
    git add -A 2>&1 | Out-Null
    git commit -m "Initial GHCAdmin setup" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "git commit returned a non-zero exit code — files may already be committed."
    } else {
        Write-OK "Initial commit created."
    }
}
catch {
    Write-Warn "Could not create initial commit: $_"
}
Pop-Location

# ── Desktop shortcut ─────────────────────────────────────────────────────────

Write-Step "Creating desktop shortcut..."
$desktopPath   = [Environment]::GetFolderPath("Desktop")
$shortcutFile  = Join-Path $desktopPath "GHCAdmin ToDo.lnk"

# Resolve the VS Code executable path
$codePath = (Get-Command code -ErrorAction SilentlyContinue).Source
if (-not $codePath) {
    # Fall back to common install locations
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe",
        "$env:ProgramFiles\Microsoft VS Code\Code.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $codePath = $c; break }
    }
}

if ($codePath) {
    try {
        $shell    = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutFile)
        $shortcut.TargetPath       = $codePath
        $shortcut.Arguments        = "`"$TargetPath`""
        $shortcut.WorkingDirectory = $TargetPath
        $shortcut.Description      = "Open GHCAdmin ToDo in VS Code"
        $shortcut.Save()
        Write-OK "Desktop shortcut created: GHCAdmin ToDo.lnk"
    }
    catch {
        Write-Warn "Could not create desktop shortcut: $_"
    }
} else {
    Write-Warn "Could not locate VS Code executable — skipping shortcut creation."
}

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "  Installation complete!"                      -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Open this folder in VS Code to get started:" -ForegroundColor White
Write-Host "  $TargetPath"                                  -ForegroundColor Cyan
Write-Host ""
Write-Host "  Or double-click 'GHCAdmin ToDo' on your Desktop." -ForegroundColor Gray
Write-Host ""
