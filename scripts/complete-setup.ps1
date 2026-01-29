# Complete IDE Setup for PROJETO PROTONS
# Run this after all configuration files are created

param(
    [switch]$SkipHooks = $false,
    [switch]$SkipExtensions = $false
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot

Write-Host "🚀 Starting PROJETO PROTONS IDE Setup..." -ForegroundColor Cyan
Write-Host ""

# Step 1: Verify Git
Write-Host "1️⃣ Checking Git installation..." -ForegroundColor Yellow
$gitVersion = git --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Git found: $gitVersion" -ForegroundColor Green
} else {
    Write-Host "   ❌ Git not found. Please install Git first." -ForegroundColor Red
    exit 1
}

# Step 2: Initialize Git (if needed)
Write-Host "2️⃣ Initializing Git repository..." -ForegroundColor Yellow
$gitDir = Join-Path $projectRoot ".git"
if (-not (Test-Path $gitDir)) {
    git init $projectRoot
    Write-Host "   ✅ Git repository initialized" -ForegroundColor Green
} else {
    Write-Host "   ✅ Git repository already exists" -ForegroundColor Green
}

# Step 3: Configure Git
Write-Host "3️⃣ Configuring Git settings..." -ForegroundColor Yellow
git config core.autocrlf true
git config core.ignorecase false
$templatePath = Join-Path $projectRoot ".gitmessage"
if (Test-Path $templatePath) {
    git config commit.template $templatePath
    Write-Host "   ✅ Git commit template configured" -ForegroundColor Green
}

# Step 4: Install Git Hooks
if (-not $SkipHooks) {
    Write-Host "4️⃣ Installing Git pre-commit hooks..." -ForegroundColor Yellow
    $hookScript = Join-Path $projectRoot "scripts\setup-git-hooks.ps1"
    if (Test-Path $hookScript) {
        & $hookScript
        Write-Host "   ✅ Pre-commit hooks installed" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  Hook script not found, skipping..." -ForegroundColor Yellow
    }
} else {
    Write-Host "4️⃣ Skipping Git hooks installation" -ForegroundColor Gray
}

# Step 5: Verify Configuration Files
Write-Host "5️⃣ Verifying configuration files..." -ForegroundColor Yellow
$requiredFiles = @(
    ".gitignore",
    ".cursorrules",
    ".editorconfig",
    ".env.example",
    ".vscode\settings.json",
    ".vscode\extensions.json",
    ".vscode\tasks.json",
    ".vscode\launch.json",
    "README.md"
)

$missing = @()
foreach ($file in $requiredFiles) {
    $fullPath = Join-Path $projectRoot $file
    if (Test-Path $fullPath) {
        Write-Host "   ✅ $file" -ForegroundColor Green
    } else {
        Write-Host "   ❌ $file (MISSING)" -ForegroundColor Red
        $missing += $file
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "   ⚠️  Some configuration files are missing!" -ForegroundColor Red
    Write-Host "   Please create these files before continuing." -ForegroundColor Yellow
    exit 1
}

# Step 6: Create .env from template
Write-Host "6️⃣ Creating local environment file..." -ForegroundColor Yellow
$envExample = Join-Path $projectRoot ".env.example"
$envFile = Join-Path $projectRoot ".env"
if (-not (Test-Path $envFile) -and (Test-Path $envExample)) {
    Copy-Item -Path $envExample -Destination $envFile
    Write-Host "   ✅ .env created from template" -ForegroundColor Green
    Write-Host "   ℹ️  Please edit .env with your local settings" -ForegroundColor Cyan
} else {
    Write-Host "   ℹ️  .env already exists or template missing" -ForegroundColor Gray
}

# Step 7: Install VSCode Extensions
if (-not $SkipExtensions) {
    Write-Host "7️⃣ Checking VSCode extensions..." -ForegroundColor Yellow
    $codeCmd = Get-Command code -ErrorAction SilentlyContinue
    if ($codeCmd) {
        $extensionsFile = Join-Path $projectRoot ".vscode\extensions.json"
        if (Test-Path $extensionsFile) {
            $extensions = Get-Content $extensionsFile | ConvertFrom-Json
            foreach ($ext in $extensions.recommendations) {
                Write-Host "   Installing $ext..." -ForegroundColor Gray
                code --install-extension $ext --force 2>&1 | Out-Null
            }
            Write-Host "   ✅ VSCode extensions installed" -ForegroundColor Green
        }
    } else {
        Write-Host "   ⚠️  VSCode not found in PATH, skipping extensions" -ForegroundColor Yellow
    }
} else {
    Write-Host "7️⃣ Skipping VSCode extensions installation" -ForegroundColor Gray
}

# Step 8: Verify Documentation Mirror
Write-Host "8️⃣ Verifying documentation structure..." -ForegroundColor Yellow
$verifyScript = Join-Path $projectRoot "scripts\verify-doc-mirror.ps1"
if (Test-Path $verifyScript) {
    & $verifyScript
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Documentation structure verified" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  Documentation issues detected (expected in planning phase)" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  Verification script not found" -ForegroundColor Yellow
}

# Step 9: Summary
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "✅ IDE Setup Complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "📋 Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Configure Git user if not already done:" -ForegroundColor White
Write-Host "     git config user.name 'Your Name'" -ForegroundColor Gray
Write-Host "     git config user.email 'your@email.com'" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Edit .env with your local settings" -ForegroundColor White
Write-Host ""
Write-Host "  3. Install Visual Studio 2022 (recommended for WPF development)" -ForegroundColor White
Write-Host ""
Write-Host "  4. Read README.md for development guidelines" -ForegroundColor White
Write-Host ""
Write-Host "  5. When ready to code, create .sln and .csproj files" -ForegroundColor White
Write-Host ""
Write-Host "📚 Documentation: documentos/doc_login/" -ForegroundColor Cyan
Write-Host "🔧 Scripts: scripts/" -ForegroundColor Cyan
Write-Host "⚙️  Configuration: .vscode/, .cursorrules, .editorconfig" -ForegroundColor Cyan
Write-Host ""
