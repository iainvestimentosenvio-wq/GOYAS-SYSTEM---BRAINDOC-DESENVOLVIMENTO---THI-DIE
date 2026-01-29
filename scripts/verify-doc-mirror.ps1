# Verify Documentation Mirror - PROJETO PROTONS
# Checks that all C# files have corresponding .md documentation

param(
    [switch]$Fix = $false
)

Write-Host "🔍 Verifying documentation mirror..." -ForegroundColor Cyan

$projectRoot = Split-Path $PSScriptRoot
$appPath = Join-Path $projectRoot "App"
$docPath = Join-Path $projectRoot "documentos\doc_login"

# Find all C# files
$csFiles = Get-ChildItem -Path $appPath -Recurse -Filter "*.cs" -File -ErrorAction SilentlyContinue

if ($null -eq $csFiles -or $csFiles.Count -eq 0) {
    Write-Host "✅ No C# files found (project in planning phase)" -ForegroundColor Green
    exit 0
}

$missingDocs = @()
$emptyDocs = @()

foreach ($csFile in $csFiles) {
    # Calculate expected .md path
    $relativePath = $csFile.FullName.Substring($appPath.Length + 1)
    $mdPath = Join-Path $docPath "App\$($relativePath -replace '\.cs$', '.md')"

    if (-not (Test-Path $mdPath)) {
        $missingDocs += @{
            CsFile = $csFile.FullName
            MdFile = $mdPath
        }

        if ($Fix) {
            # Create directory if needed
            $mdDir = Split-Path $mdPath
            if (-not (Test-Path $mdDir)) {
                New-Item -ItemType Directory -Path $mdDir -Force | Out-Null
            }

            # Copy template
            $template = Join-Path $projectRoot "documentos\TEMPLATES\TEMPLATE_DOC_ARQUIVO.md"
            if (Test-Path $template) {
                Copy-Item -Path $template -Destination $mdPath
                Write-Host "  ✅ Created $mdPath" -ForegroundColor Green
            }
        }
    } else {
        # Check if doc is empty
        $content = Get-Content $mdPath -Raw
        if (-not ($content -match "## Objetivo" -and $content -match "## Como funciona")) {
            $emptyDocs += $mdPath
        }
    }
}

# Report
if ($missingDocs.Count -eq 0 -and $emptyDocs.Count -eq 0) {
    Write-Host "✅ All C# files have proper documentation!" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "❌ Documentation mirror issues found:" -ForegroundColor Red

    if ($missingDocs.Count -gt 0) {
        Write-Host ""
        Write-Host "Missing documentation files:" -ForegroundColor Yellow
        foreach ($missing in $missingDocs) {
            Write-Host "  $($missing.CsFile)" -ForegroundColor White
            Write-Host "    → $($missing.MdFile)" -ForegroundColor Gray
        }
    }

    if ($emptyDocs.Count -gt 0) {
        Write-Host ""
        Write-Host "Empty/incomplete documentation files:" -ForegroundColor Yellow
        foreach ($empty in $emptyDocs) {
            Write-Host "  $empty" -ForegroundColor White
        }
    }

    if (-not $Fix) {
        Write-Host ""
        Write-Host "💡 Run with -Fix flag to auto-generate missing docs from template" -ForegroundColor Cyan
    }

    exit 1
}
