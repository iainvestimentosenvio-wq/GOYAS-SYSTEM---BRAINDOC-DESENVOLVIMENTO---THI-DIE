# Prepare assets visuais do instalador Inno (PT-BR).
# Gera imagens light/dark sem distorcao para o wizard moderno dinamico.

[CmdletBinding()]
param(
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $ProjectRoot) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if (-not $scriptPath) {
        throw 'Nao foi possivel resolver o caminho do script para inferir ProjectRoot.'
    }
    $scriptDir = Split-Path -Parent $scriptPath
    # windows/scripts -> windows -> INSTALADOR -> <repo-root>
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptDir))
}

$loginBgPath = Join-Path $ProjectRoot 'Login\Protons.UI\Assets\Brand\login_bg.png'
$loginLogoPath = Join-Path $ProjectRoot 'Login\Protons.UI\Assets\Brand\logo_protons.png'
$outputDir = Join-Path $ProjectRoot 'INSTALADOR\windows\ativos\installer'

$wizardBackLightPath = Join-Path $outputDir 'wizard_back_light.png'
$wizardBackDarkPath = Join-Path $outputDir 'wizard_back_dark.png'
$wizardSmallLogoLightPath = Join-Path $outputDir 'wizard_small_logo_light.png'
$wizardSmallLogoDarkPath = Join-Path $outputDir 'wizard_small_logo_dark.png'

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label nao encontrado: $Path"
    }
}

function New-BitmapFromFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    return New-Object System.Drawing.Bitmap($Path)
}

function New-Graphics {
    param([Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap)
    $graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return $graphics
}

function New-CoverBitmap {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][int]$TargetWidth,
        [Parameter(Mandatory = $true)][int]$TargetHeight
    )

    $source = $null
    $canvas = $null
    $graphics = $null

    try {
        $source = New-BitmapFromFile -Path $SourcePath

        $srcWidth = [double]$source.Width
        $srcHeight = [double]$source.Height
        $targetRatio = [double]$TargetWidth / [double]$TargetHeight
        $sourceRatio = $srcWidth / $srcHeight

        if ($sourceRatio -gt $targetRatio) {
            $cropWidth = [Math]::Max(1, [int][Math]::Round($srcHeight * $targetRatio))
            $cropHeight = [int]$srcHeight
            $cropX = [int][Math]::Floor(($srcWidth - $cropWidth) / 2.0)
            $cropY = 0
        } else {
            $cropWidth = [int]$srcWidth
            $cropHeight = [Math]::Max(1, [int][Math]::Round($srcWidth / $targetRatio))
            $cropX = 0
            $cropY = [int][Math]::Floor(($srcHeight - $cropHeight) / 2.0)
        }

        $canvas = New-Object System.Drawing.Bitmap($TargetWidth, $TargetHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = New-Graphics -Bitmap $canvas
        $destinationRect = New-Object System.Drawing.Rectangle(0, 0, $TargetWidth, $TargetHeight)
        $sourceRect = New-Object System.Drawing.Rectangle($cropX, $cropY, $cropWidth, $cropHeight)
        $graphics.DrawImage($source, $destinationRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)

        return $canvas
    } finally {
        if ($graphics) { $graphics.Dispose() }
        if ($source) { $source.Dispose() }
    }
}

function New-ContainBitmap {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][int]$TargetWidth,
        [Parameter(Mandatory = $true)][int]$TargetHeight
    )

    $source = $null
    $canvas = $null
    $graphics = $null

    try {
        $source = New-BitmapFromFile -Path $SourcePath

        $srcWidth = [double]$source.Width
        $srcHeight = [double]$source.Height
        $scale = [Math]::Min([double]$TargetWidth / $srcWidth, [double]$TargetHeight / $srcHeight)

        $drawWidth = [Math]::Max(1, [int][Math]::Round($srcWidth * $scale))
        $drawHeight = [Math]::Max(1, [int][Math]::Round($srcHeight * $scale))
        $offsetX = [int][Math]::Floor(($TargetWidth - $drawWidth) / 2.0)
        $offsetY = [int][Math]::Floor(($TargetHeight - $drawHeight) / 2.0)

        $canvas = New-Object System.Drawing.Bitmap($TargetWidth, $TargetHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = New-Graphics -Bitmap $canvas
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $destinationRect = New-Object System.Drawing.Rectangle($offsetX, $offsetY, $drawWidth, $drawHeight)
        $sourceRect = New-Object System.Drawing.Rectangle(0, 0, [int]$source.Width, [int]$source.Height)
        $graphics.DrawImage($source, $destinationRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)

        return $canvas
    } finally {
        if ($graphics) { $graphics.Dispose() }
        if ($source) { $source.Dispose() }
    }
}

function Add-DarkOverlayInPlace {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][int]$Alpha,
        [Parameter(Mandatory = $true)][int]$Red,
        [Parameter(Mandatory = $true)][int]$Green,
        [Parameter(Mandatory = $true)][int]$Blue
    )

    $graphics = $null
    $brush = $null

    try {
        $graphics = New-Graphics -Bitmap $Bitmap
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($Alpha, $Red, $Green, $Blue))
        $graphics.FillRectangle($brush, 0, 0, $Bitmap.Width, $Bitmap.Height)
    } finally {
        if ($brush) { $brush.Dispose() }
        if ($graphics) { $graphics.Dispose() }
    }
}

function New-ColorAdjustedBitmap {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$SourceBitmap,
        [Parameter(Mandatory = $true)][double]$Gain,
        [Parameter(Mandatory = $true)][double]$Offset
    )

    $canvas = $null
    $graphics = $null
    $imageAttributes = $null

    try {
        $canvas = New-Object System.Drawing.Bitmap($SourceBitmap.Width, $SourceBitmap.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = New-Graphics -Bitmap $canvas
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $matrix = New-Object System.Drawing.Imaging.ColorMatrix
        $matrix.Matrix00 = [single]$Gain
        $matrix.Matrix11 = [single]$Gain
        $matrix.Matrix22 = [single]$Gain
        $matrix.Matrix33 = 1.0
        $matrix.Matrix40 = [single]$Offset
        $matrix.Matrix41 = [single]$Offset
        $matrix.Matrix42 = [single]$Offset
        $matrix.Matrix44 = 1.0

        $imageAttributes = New-Object System.Drawing.Imaging.ImageAttributes
        $imageAttributes.SetColorMatrix($matrix, [System.Drawing.Imaging.ColorMatrixFlag]::Default, [System.Drawing.Imaging.ColorAdjustType]::Bitmap)

        $rect = New-Object System.Drawing.Rectangle(0, 0, $SourceBitmap.Width, $SourceBitmap.Height)
        $graphics.DrawImage($SourceBitmap, $rect, 0, 0, $SourceBitmap.Width, $SourceBitmap.Height, [System.Drawing.GraphicsUnit]::Pixel, $imageAttributes)

        return $canvas
    } finally {
        if ($imageAttributes) { $imageAttributes.Dispose() }
        if ($graphics) { $graphics.Dispose() }
    }
}

function Save-Png {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

try {
    Add-Type -AssemblyName System.Drawing

    Assert-FileExists -Path $loginBgPath -Label 'Imagem de fundo do login'
    Assert-FileExists -Path $loginLogoPath -Label 'Logo do login'

    New-Item -Path $outputDir -ItemType Directory -Force | Out-Null

    $backLight = New-CoverBitmap -SourcePath $loginBgPath -TargetWidth 140 -TargetHeight 459
    $backDark = New-CoverBitmap -SourcePath $loginBgPath -TargetWidth 140 -TargetHeight 459
    $logoLight = New-ContainBitmap -SourcePath $loginLogoPath -TargetWidth 55 -TargetHeight 55
    $logoDarkBase = New-ContainBitmap -SourcePath $loginLogoPath -TargetWidth 55 -TargetHeight 55
    $logoDark = $null

    try {
        # Tema dark: mesma base com overlay controlado para legibilidade.
        Add-DarkOverlayInPlace -Bitmap $backDark -Alpha 106 -Red 12 -Green 16 -Blue 25
        Add-DarkOverlayInPlace -Bitmap $backDark -Alpha 32 -Red 8 -Green 12 -Blue 18

        # Logo dark: elevar contraste/ganho sem perder transparencia.
        $logoDark = New-ColorAdjustedBitmap -SourceBitmap $logoDarkBase -Gain 1.12 -Offset 0.05

        Save-Png -Bitmap $backLight -Path $wizardBackLightPath
        Save-Png -Bitmap $backDark -Path $wizardBackDarkPath
        Save-Png -Bitmap $logoLight -Path $wizardSmallLogoLightPath
        Save-Png -Bitmap $logoDark -Path $wizardSmallLogoDarkPath
    } finally {
        if ($logoDark) { $logoDark.Dispose() }
        if ($logoDarkBase) { $logoDarkBase.Dispose() }
        if ($logoLight) { $logoLight.Dispose() }
        if ($backDark) { $backDark.Dispose() }
        if ($backLight) { $backLight.Dispose() }
    }

    foreach ($path in @($wizardBackLightPath, $wizardBackDarkPath, $wizardSmallLogoLightPath, $wizardSmallLogoDarkPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Arquivo de saida esperado nao foi gerado: $path"
        }
    }

    Write-Host 'Assets UX dinamicos do Inno gerados com sucesso.'
    foreach ($path in @($wizardBackLightPath, $wizardBackDarkPath, $wizardSmallLogoLightPath, $wizardSmallLogoDarkPath)) {
        $item = Get-Item -LiteralPath $path
        Write-Host " - $($item.FullName) ($($item.Length) bytes)"
    }
    exit 0
} catch {
    Write-Error "Falha ao preparar assets UX do Inno: $($_.Exception.Message)"
    exit 1
}
