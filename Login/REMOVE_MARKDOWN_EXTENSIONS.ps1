# PowerShell script to remove problematic markdown extensions from VSCode
# These extensions cause false positives on .md documentation files

Write-Host "🗑️  Removing problematic markdown extensions..." -ForegroundColor Cyan
Write-Host ""

# Function to uninstall extension
function Remove-Extension {
    param([string]$ExtensionId)
    Write-Host "  Removing: $ExtensionId" -ForegroundColor Yellow
    code --uninstall-extension $ExtensionId
}

# Uninstall extensions that cause false positives
Remove-Extension "streetsidesoftware.code-spell-checker"
Remove-Extension "davidanson.vscode-markdownlint"
Remove-Extension "yzhang.markdown-all-in-one"

Write-Host ""
Write-Host "✅ Extensions removed!" -ForegroundColor Green
Write-Host ""
Write-Host "🔄 Reloading VSCode..." -ForegroundColor Cyan
Write-Host "   Ctrl+Shift+P → Developer: Reload Window" -ForegroundColor Gray
Write-Host ""
Write-Host "✨ Markdown files should now have NO false positives!" -ForegroundColor Green
Write-Host ""
Write-Host "💡 Tip: If problems still appear, clear VSCode cache:" -ForegroundColor Yellow
Write-Host "   rmdir %APPDATA%\Code\Cache /s /q" -ForegroundColor Gray
Write-Host "   rmdir %APPDATA%\Code\CachedData /s /q" -ForegroundColor Gray
