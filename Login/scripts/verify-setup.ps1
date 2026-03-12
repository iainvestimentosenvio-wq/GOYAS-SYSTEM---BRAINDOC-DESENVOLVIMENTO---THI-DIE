# Verify PROJETO PROTONS Setup
# Verify that IDE setup is complete and correct

$projectRoot = Split-Path $PSScriptRoot
$allGood = $true

Write-Host "🔍 Verifying PROJETO PROTONS Setup..." -ForegroundColor Cyan
Write-Host ""

# Check 1: Git
Write-Host "Git Repository:" -ForegroundColor Yellow
if (Test-Path (Join-Path $projectRoot ".git")) {
    Write-Host "  ✅ Initialized" -ForegroundColor Green
} else {
    Write-Host "  ❌ Not initialized" -ForegroundColor Red
    $allGood = $false
}

# Check 2: Git Hooks
Write-Host "Git Hooks:" -ForegroundColor Yellow
if (Test-Path (Join-Path $projectRoot ".git\hooks\pre-commit")) {
    Write-Host "  ✅ Pre-commit hook installed" -ForegroundColor Green
} else {
    Write-Host "  ❌ Pre-commit hook missing" -ForegroundColor Red
    $allGood = $false
}

# Check 3: Config Files
Write-Host "Configuration Files:" -ForegroundColor Yellow
$configs = @(
    ".gitignore",
    ".cursorrules",
    ".editorconfig",
    ".vscode\settings.json"
)
foreach ($cfg in $configs) {
    if (Test-Path (Join-Path $projectRoot $cfg)) {
        Write-Host "  ✅ $cfg" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $cfg missing" -ForegroundColor Red
        $allGood = $false
    }
}

# Check 4: Scripts
Write-Host "Utility Scripts:" -ForegroundColor Yellow
$scripts = @(
    "scripts\verify-doc-mirror.ps1",
    "scripts\setup-git-hooks.ps1"
)
foreach ($script in $scripts) {
    if (Test-Path (Join-Path $projectRoot $script)) {
        Write-Host "  ✅ $script" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $script missing" -ForegroundColor Red
        $allGood = $false
    }
}

# Check 5: .env
Write-Host "Environment:" -ForegroundColor Yellow
if (Test-Path (Join-Path $projectRoot ".env")) {
    Write-Host "  ✅ .env configured" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  .env not created (run setup or copy .env.example)" -ForegroundColor Yellow
}

# Summary
Write-Host ""
if ($allGood) {
    Write-Host "✅ Setup is complete and valid!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "❌ Setup has issues. Please fix the errors above." -ForegroundColor Red
    exit 1
}
