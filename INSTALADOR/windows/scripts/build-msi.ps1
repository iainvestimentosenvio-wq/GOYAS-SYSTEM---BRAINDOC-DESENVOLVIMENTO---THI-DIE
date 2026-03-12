# Build MSI usando WiX Toolset v4
# Proteção (Salmo 121:8): "O Senhor guardará a tua saída e a tua entrada, desde agora e para sempre."
# Requer: .NET SDK 8.0 + WiX Toolset instalado

[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

function Test-RunningOnWindows {
    try {
        return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows
        )
    } catch {
        return ($env:OS -eq 'Windows_NT')
    }
}

if (-not (Test-RunningOnWindows)) {
    Write-Host "❌ ERRO: build-msi.ps1 deve ser executado em Windows." -ForegroundColor Red
    Write-Host "   Motivo: WiX/Heat depende de kernel32.dll e nao roda nativamente no Linux." -ForegroundColor Yellow
    Write-Host "   Acao: execute este script dentro de ambiente Windows (host/VM)." -ForegroundColor Gray
    exit 1
}

# Determinar caminhos
$ScriptRoot = Split-Path -Parent $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)

# OB-01: Carregar utilitarios de log
$LogUtilsPath = Join-Path $ProjectRoot "INSTALADOR\comum\scripts\Log-Utils.ps1"
if (Test-Path $LogUtilsPath) {
    . $LogUtilsPath
    Initialize-BuildLog -ScriptName "build-msi" -LogDir (Join-Path $ProjectRoot "INSTALADOR\saida\logs")
} else {
    # Fallback se Log-Utils nao existir
    function Write-Log { param($Level, $Message); Write-Host "[$Level] $Message" }
    function Start-BuildStep { param($StepName); Write-Host "[STEP] Iniciando: $StepName" }
    function Complete-BuildStep { param($StepName); Write-Host "[OK] $StepName concluido" }
    function New-FileChecksum { param($FilePath); return $null }
    function Complete-BuildLog { param($Status, $Artifact, $ArtifactSize, $Checksum) }
    function Add-DurationsToBuildInfo { param($BuildInfoPath) }
}

function Require-DotNet8 {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Host "❌ ERRO: dotnet não encontrado no PATH" -ForegroundColor Red
        exit 1
    }

    $sdks = & dotnet --list-sdks 2>$null
    if (-not $sdks) {
        Write-Host "❌ ERRO: não foi possível listar SDKs do .NET" -ForegroundColor Red
        exit 1
    }

    if ($sdks -notmatch '^8\.') {
        Write-Host "❌ ERRO: .NET SDK 8.x não encontrado" -ForegroundColor Red
        exit 1
    }
}

function Invoke-DotNetWithTimeout {
    param(
        [string[]]$DotNetArgs,
        [int]$TimeoutSec,
        [string]$StepName
    )

    # Garantir que não haja argumentos nulos/vazios
    $cleanArgs = @()
    foreach ($a in $DotNetArgs) {
        if ($null -ne $a -and $a -ne "") { $cleanArgs += $a }
    }
    if ($cleanArgs.Count -eq 0) {
        Write-Host "❌ ERRO: Argumentos do dotnet vazios em $StepName" -ForegroundColor Red
        exit 1
    }

    $proc = Start-Process -FilePath "dotnet" -ArgumentList $cleanArgs -PassThru -NoNewWindow
    $exited = $proc.WaitForExit($TimeoutSec * 1000)
    if (-not $exited) {
        try { $proc.Kill() } catch {}
        Write-Host "❌ ERRO: Timeout (${TimeoutSec}s) em $StepName" -ForegroundColor Red
        exit 1
    }

    $exitCode = if ($null -eq $proc.ExitCode) { 0 } else { $proc.ExitCode }
    if ($exitCode -ne 0) {
        Write-Host "❌ ERRO em $StepName (ExitCode=$exitCode)" -ForegroundColor Red
        exit 1
    }
}

# === SS-04: Assinatura digital com SignTool ===
# Requer: Windows SDK instalado + certificado Code Signing
# Variaveis de ambiente:
#   PROTONS_CERT_PATH - caminho do arquivo .pfx
#   PROTONS_CERT_PASS - senha do certificado

function Find-SignTool {
    # Procurar signtool no Windows SDK
    $sdkPaths = @(
        "C:\Program Files (x86)\Windows Kits\10\bin",
        "C:\Program Files\Windows Kits\10\bin"
    )

    foreach ($sdkPath in $sdkPaths) {
        if (Test-Path $sdkPath) {
            $signTool = Get-ChildItem -Path $sdkPath -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
                        Where-Object { $_.FullName -match "x64" } |
                        Sort-Object { $_.Directory.Name } -Descending |
                        Select-Object -First 1 -ExpandProperty FullName
            if ($signTool) {
                return $signTool
            }
        }
    }

    # Tentar no PATH
    $inPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($inPath) {
        return $inPath.Source
    }

    return $null
}

function Sign-Artifact {
    param(
        [Parameter(Mandatory=$true)]
        [string]$FilePath,
        [string]$TimestampUrl = "http://timestamp.digicert.com"
    )

    $CertPath = $env:PROTONS_CERT_PATH
    $CertPass = $env:PROTONS_CERT_PASS

    # Verificar se certificado esta configurado
    if (-not $CertPath) {
        Write-Host "⚠️  PROTONS_CERT_PATH não configurado - artefato NÃO assinado" -ForegroundColor Yellow
        Write-Host "   Para assinar, defina as variáveis de ambiente:" -ForegroundColor Gray
        Write-Host "   `$env:PROTONS_CERT_PATH = 'C:\caminho\certificado.pfx'" -ForegroundColor Gray
        Write-Host "   `$env:PROTONS_CERT_PASS = 'senha'" -ForegroundColor Gray
        return $false
    }

    if (-not (Test-Path $CertPath)) {
        Write-Host "⚠️  Certificado não encontrado: $CertPath" -ForegroundColor Yellow
        Write-Host "   Artefato NÃO assinado" -ForegroundColor Yellow
        return $false
    }

    # Localizar signtool
    $SignTool = Find-SignTool
    if (-not $SignTool) {
        Write-Host "⚠️  SignTool não encontrado" -ForegroundColor Yellow
        Write-Host "   Instale o Windows SDK para habilitar assinatura" -ForegroundColor Gray
        Write-Host "   https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Gray
        return $false
    }

    Write-Host "[4/5] Assinando $(Split-Path -Leaf $FilePath)..." -ForegroundColor Green
    Write-Host "   SignTool: $SignTool" -ForegroundColor Gray
    Write-Host "   Timestamp: $TimestampUrl" -ForegroundColor Gray

    # Executar assinatura
    $signArgs = @(
        "sign",
        "/f", $CertPath,
        "/fd", "SHA256",
        "/tr", $TimestampUrl,
        "/td", "SHA256",
        "/v",
        $FilePath
    )

    # Adicionar senha se fornecida
    if ($CertPass) {
        $signArgs = @("sign", "/f", $CertPath, "/p", $CertPass, "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256", "/v", $FilePath)
    }

    try {
        & $SignTool $signArgs 2>&1 | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }

        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Assinatura concluída com sucesso" -ForegroundColor Green
            return $true
        } else {
            Write-Host "❌ ERRO na assinatura (código $LASTEXITCODE)" -ForegroundColor Red
            return $false
        }
    } catch {
        Write-Host "❌ ERRO ao executar SignTool: $_" -ForegroundColor Red
        return $false
    }
}

function Verify-Signature {
    param([string]$FilePath)

    $SignTool = Find-SignTool
    if (-not $SignTool) { return $false }

    Write-Host "Verificando assinatura..." -ForegroundColor Gray
    & $SignTool verify /pa $FilePath 2>&1 | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }

    return ($LASTEXITCODE -eq 0)
}

Write-Host "=== Build Protons MSI ===" -ForegroundColor Cyan
Write-Host ""

# Verificar se estamos na raiz do projeto
$ProjectRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
Set-Location $ProjectRoot

Write-Host "Diretório do projeto: $ProjectRoot" -ForegroundColor Yellow
Write-Host ""

# QB-03: Inicializar estrutura de diretórios de saída
$InitDirsScript = Join-Path $ProjectRoot "INSTALADOR\comum\scripts\Init-Dirs.ps1"
if (Test-Path $InitDirsScript) {
    . $InitDirsScript -ProjectRoot $ProjectRoot
} else {
    Write-Host "⚠️  Init-Dirs.ps1 não encontrado, criando diretórios manualmente" -ForegroundColor Yellow
    $OutputWindows = "$ProjectRoot\INSTALADOR\saida\windows"
    $OutputMetadata = "$ProjectRoot\INSTALADOR\saida\metadata"
    if (-not (Test-Path $OutputWindows)) { New-Item -ItemType Directory -Path $OutputWindows -Force | Out-Null }
    if (-not (Test-Path $OutputMetadata)) { New-Item -ItemType Directory -Path $OutputMetadata -Force | Out-Null }
}
Write-Host ""

# Pre-requisitos
Require-DotNet8

# === VC-02: Ler versao do arquivo centralizado ===
$VersionFile = Join-Path $ProjectRoot "INSTALADOR\comum\version.env"
if (-not (Test-Path $VersionFile)) {
    Write-Host "❌ ERRO: version.env nao encontrado: $VersionFile" -ForegroundColor Red
    exit 1
}

$VersionContent = Get-Content $VersionFile
$VERSION = ($VersionContent | Select-String '^VERSION=' | ForEach-Object { ($_ -replace '^VERSION=', '').Trim('"') })
$PRODUCT_NAME = ($VersionContent | Select-String '^PRODUCT_NAME=' | ForEach-Object { ($_ -replace '^PRODUCT_NAME=', '').Trim('"') })
$PUBLISHER = ($VersionContent | Select-String '^PUBLISHER=' | ForEach-Object { ($_ -replace '^PUBLISHER=', '').Trim('"') })

if (-not $VERSION) {
    Write-Host "❌ ERRO: VERSION nao definida em $VersionFile" -ForegroundColor Red
    exit 1
}

Write-Host "Versao: $VERSION" -ForegroundColor Green
Write-Host "Produto: $PRODUCT_NAME" -ForegroundColor Green
Write-Host "Publisher: $PUBLISHER" -ForegroundColor Green
Write-Host ""

$env:PROTONS_VERSION = $VERSION

# === RP-01: Reprodutibilidade ===
$env:TZ = "UTC"

$epoch = if (Get-Command git -ErrorAction SilentlyContinue) {
    try { [int](& git log -1 --format=%ct) } catch { [int][double]::Parse((Get-Date -UFormat %s)) }
} else {
    [int][double]::Parse((Get-Date -UFormat %s))
}
$env:SOURCE_DATE_EPOCH = "$epoch"

$BuildMetadataDir = Join-Path $ProjectRoot "INSTALADOR\saida\metadata"
if (-not (Test-Path $BuildMetadataDir)) { New-Item -ItemType Directory -Path $BuildMetadataDir | Out-Null }
$gitCommit = "unknown"
if (Get-Command git -ErrorAction SilentlyContinue) {
    try { $gitCommit = (& git rev-parse --short HEAD).Trim() } catch {}
}
$gitDirty = 1
if (Get-Command git -ErrorAction SilentlyContinue) {
    try { $gitDirty = if ((& git status --porcelain).Length -eq 0) { 0 } else { 1 } } catch {}
}
$buildUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

@(
    "VERSION=$VERSION",
    "SOURCE_DATE_EPOCH=$env:SOURCE_DATE_EPOCH",
    "GIT_COMMIT=$gitCommit",
    "GIT_DIRTY=$gitDirty",
    "BUILD_UTC=$buildUtc"
) | Set-Content -Path (Join-Path $BuildMetadataDir "build-info.txt") -Encoding UTF8

# 1. Publicar aplicação .NET (ou reutilizar publish existente)
if ($SkipPublish) {
    Write-Log INFO "SkipPublish ativo: reutilizando publish existente em Login\\Protons.UI\\bin\\Release\\net8.0\\win-x64\\publish"
} else {
    Start-BuildStep -StepName "Publish .NET"
    Write-Log STEP "Publicando Protons.UI..."
    Invoke-DotNetWithTimeout -DotNetArgs @(
        "publish",
        "Login\Protons.UI\Protons.UI.csproj",
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:ContinuousIntegrationBuild=true",
        "-p:Deterministic=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    ) -TimeoutSec 1800 -StepName "dotnet publish"

    Write-Log OK "Publicação concluída"
    Complete-BuildStep -StepName "Publish .NET"
}

# QB-01: Validar que publish foi bem-sucedido
$PublishDir = "$ProjectRoot\Login\Protons.UI\bin\Release\net8.0\win-x64\publish"
$RequiredFiles = @(
    "Protons.UI.exe",
    "Protons.UI.dll",
    "Protons.UI.runtimeconfig.json"
)

Write-Host "Validando diretório de publish..." -ForegroundColor Gray
if (-not (Test-Path $PublishDir)) {
    Write-Host "❌ ERRO: Diretório de publish não encontrado: $PublishDir" -ForegroundColor Red
    if ($SkipPublish) {
        Write-Host "   SkipPublish foi usado, mas o publish nao existe no caminho esperado." -ForegroundColor Yellow
    } else {
        Write-Host "   O comando 'dotnet publish' pode ter falhado silenciosamente." -ForegroundColor Yellow
    }
    exit 1
}

$MissingFiles = @()
foreach ($file in $RequiredFiles) {
    $filePath = Join-Path $PublishDir $file
    if (-not (Test-Path $filePath)) {
        $MissingFiles += $file
    }
}

if ($MissingFiles.Count -gt 0) {
    Write-Host "❌ ERRO: Arquivos obrigatórios ausentes no publish:" -ForegroundColor Red
    foreach ($f in $MissingFiles) {
        Write-Host "   - $f" -ForegroundColor Yellow
    }
    exit 1
}

$PublishFileCount = (Get-ChildItem -Path $PublishDir -File).Count
$PublishSize = [math]::Round((Get-ChildItem -Path $PublishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
Write-Host "✅ Publish validado: $PublishFileCount arquivos, ${PublishSize}MB" -ForegroundColor Green
Write-Host ""

# 2. Verificar se .ico existe
$IcoPath = "$ProjectRoot\INSTALADOR\windows\ativos\logo_protons.ico"
if (-not (Test-Path $IcoPath)) {
    Write-Host "⚠️  AVISO: logo_protons.ico não encontrado em:" -ForegroundColor Yellow
    Write-Host "   $IcoPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   Crie o ícone .ico usando:" -ForegroundColor Yellow
    Write-Host "   - ImageMagick (Windows/WSL)" -ForegroundColor Yellow
    Write-Host "   - Online: https://convertio.co/png-ico/" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   O MSI será criado SEM ícone personalizado" -ForegroundColor Yellow
    Write-Host ""
}

# 3. Gerar Variables.wxi com versao centralizada
Write-Host "[2/5] Gerando Variables.wxi com versao $VERSION..." -ForegroundColor Green
$WxiPath = Join-Path $ProjectRoot "INSTALADOR\windows\wix\Variables.wxi"
$AssetsDir = Join-Path $PublishDir "Assets"
$HasAssets = if (Test-Path $AssetsDir) { "1" } else { "0" }
Write-Host "   Assets detectados: $HasAssets" -ForegroundColor Gray
$WxiContent = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- ARQUIVO GERADO AUTOMATICAMENTE - NAO EDITAR -->
<!-- Versao centralizada em: INSTALADOR\comum\version.env -->
<Include>
  <?define ProductName = "$PRODUCT_NAME" ?>
  <?define Publisher = "$PUBLISHER" ?>
  <?define Version = "$VERSION" ?>
  <?define HasAssets = "$HasAssets" ?>
  <?define UpgradeCode = "A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D" ?>
</Include>
"@
$WxiContent | Set-Content -Path $WxiPath -Encoding UTF8
Write-Host "✅ Variables.wxi gerado" -ForegroundColor Green

# QB-05: Gerar Protons.wixproj com versao dinamica
Write-Host "   Gerando Protons.wixproj com OutputName dinamico..." -ForegroundColor Gray
$WixprojPath = Join-Path $ProjectRoot "INSTALADOR\windows\wix\Protons.wixproj"
$WixprojContent = @"
<Project Sdk="WixToolset.Sdk/4.0.6">
  <!-- ARQUIVO GERADO AUTOMATICAMENTE - NAO EDITAR -->
  <!-- Versao centralizada em: INSTALADOR\comum\version.env -->
  <PropertyGroup>
    <OutputName>Protons-$VERSION-x64</OutputName>
    <OutputType>Package</OutputType>
    <OutputPath>bin\`$(Configuration)\</OutputPath>
    <Platform>x64</Platform>
    <InstallerPlatform>x64</InstallerPlatform>
    <PublishDir>`$(MSBuildProjectDirectory)\..\..\..\Login\Protons.UI\bin\Release\net8.0\win-x64\publish</PublishDir>
    <EnableProjectHarvesting>false</EnableProjectHarvesting>
    <DefineConstants>PublishDir=`$(PublishDir)</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WixToolset.Heat" Version="4.0.6" />
    <PackageReference Include="WixToolset.Util.wixext" Version="4.0.6" />
  </ItemGroup>

  <ItemGroup>
    <HarvestDirectory Include="`$(PublishDir)">
      <ComponentGroupName>HarvestedFiles</ComponentGroupName>
      <DirectoryRefId>INSTALLFOLDER</DirectoryRefId>
      <PreprocessorVariable>var.PublishDir</PreprocessorVariable>
      <SuppressRootDirectory>true</SuppressRootDirectory>
      <SuppressRegistry>true</SuppressRegistry>
    </HarvestDirectory>
  </ItemGroup>
</Project>
"@
$WixprojContent | Set-Content -Path $WixprojPath -Encoding UTF8
Write-Log OK "Protons.wixproj gerado com OutputName=Protons-$VERSION-x64"

# 4. Build WiX
Start-BuildStep -StepName "Build WiX"
Write-Log STEP "Compilando instalador WiX..."
Set-Location "$ProjectRoot\INSTALADOR\windows\wix"

Invoke-DotNetWithTimeout -DotNetArgs @("build", "Protons.wixproj", "-c", "Release") -TimeoutSec 1200 -StepName "dotnet build (WiX)"

Write-Log OK "Compilação WiX concluída"

# 5. Copiar MSI para output
$MsiSource = "$ProjectRoot\INSTALADOR\windows\wix\bin\Release\Protons-$VERSION-x64.msi"
$MsiOutputDir = "$ProjectRoot\INSTALADOR\saida\windows"
$MsiDest = "$MsiOutputDir\Protons-$VERSION-x64.msi"

if (-not (Test-Path $MsiOutputDir)) {
    New-Item -ItemType Directory -Path $MsiOutputDir | Out-Null
}

if (Test-Path $MsiSource) {
    Write-Log INFO "Copiando MSI para output..."
    Copy-Item -Path $MsiSource -Destination $MsiDest -Force
    Write-Log OK "MSI copiado"
} else {
    Write-Log ERROR "MSI não foi gerado em $MsiSource"
    Complete-BuildLog -Status "FAILED"
    exit 1
}
Complete-BuildStep -StepName "Build WiX"

# OB-03: Gerar checksum ANTES da assinatura
Start-BuildStep -StepName "Checksum pre-assinatura"
$checksumPreSign = New-FileChecksum -FilePath $MsiDest
Complete-BuildStep -StepName "Checksum pre-assinatura"

# Assinar MSI (SS-04)
Start-BuildStep -StepName "Assinatura digital"
$signed = Sign-Artifact -FilePath $MsiDest

if ($signed) {
    Write-Log OK "MSI assinado digitalmente"
    $verified = Verify-Signature -FilePath $MsiDest
    if ($verified) {
        Write-Log OK "Assinatura verificada"
    } else {
        Write-Log WARN "Falha na verificação da assinatura"
    }

    # OB-03: Gerar checksum APOS a assinatura (hash final)
    $checksumPostSign = New-FileChecksum -FilePath $MsiDest
    $finalChecksum = $checksumPostSign
} else {
    Write-Log WARN "MSI NÃO assinado (certificado não configurado)"
    $finalChecksum = $checksumPreSign
}
Complete-BuildStep -StepName "Assinatura digital"

# Obter tamanho do artefato
$artifactSize = "$([math]::Round((Get-Item $MsiDest).Length / 1MB, 2)) MB"

# OB-02: Adicionar duracoes ao build-info.txt
$BuildInfoPath = Join-Path $ProjectRoot "INSTALADOR\saida\metadata\build-info.txt"
Add-DurationsToBuildInfo -BuildInfoPath $BuildInfoPath

# Finalizar log com resumo
Complete-BuildLog -Status "SUCCESS" -Artifact $MsiDest -ArtifactSize $artifactSize -Checksum $finalChecksum

Write-Log INFO "-"
Write-Log INFO "Para testar a instalação:"
Write-Log INFO "  msiexec /i $MsiDest"
Write-Host ""
if (-not $signed) {
    Write-Host "Para habilitar assinatura digital:" -ForegroundColor White
    Write-Host "  `$env:PROTONS_CERT_PATH = 'C:\caminho\certificado.pfx'" -ForegroundColor Gray
    Write-Host "  `$env:PROTONS_CERT_PASS = 'senha_do_certificado'" -ForegroundColor Gray
    Write-Host ""
}
