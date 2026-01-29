# Setup Git Hooks for PROJETO PROTONS
# Run this script after git init to install pre-commit hooks

Write-Host "🔧 Setting up Git pre-commit hooks..." -ForegroundColor Cyan

$hookSource = Join-Path $PSScriptRoot "pre-commit-hook-template.sh"
$hookDest = Join-Path (Split-Path $PSScriptRoot) ".git\hooks\pre-commit"

if (-not (Test-Path $hookSource)) {
    Write-Host "❌ Error: Hook template not found at $hookSource" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path (Join-Path (Split-Path $PSScriptRoot) ".git"))) {
    Write-Host "❌ Error: Git repository not initialized. Run 'git init' first." -ForegroundColor Red
    exit 1
}

# Copy hook
Copy-Item -Path $hookSource -Destination $hookDest -Force

Write-Host "✅ Pre-commit hook installed at $hookDest" -ForegroundColor Green
Write-Host ""
Write-Host "📝 The hook will now enforce documentation mirroring on every commit." -ForegroundColor Yellow
Write-Host "   To test: git commit -m 'test'" -ForegroundColor Yellow
Write-Host ""
