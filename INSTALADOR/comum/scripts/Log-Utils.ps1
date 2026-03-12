# =============================================================================
# Log-Utils.ps1 - Utilitarios de log padronizados para scripts de build
# =============================================================================
# OB-01: Padronizar logs de build com formato consistente
#
# Uso:
#   . .\Log-Utils.ps1
#   Initialize-BuildLog -ScriptName "build-msi"
#   Write-Log INFO "Iniciando build"
#   Write-Log STEP "Publicando aplicacao"
#   Write-Log OK "Concluido"
#   Complete-BuildLog -Status "SUCCESS" -Artifact $msiPath
#
# Formato: [YYYY-MM-DD HH:MM:SS] [NIVEL] Mensagem
# =============================================================================

# Variaveis globais
$script:LogFile = $null
$script:LogStartTime = $null
$script:LogScriptName = $null
$script:StepTimes = @{}
$script:Stopwatch = $null

function Initialize-BuildLog {
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptName,
        [string]$LogDir
    )

    $script:LogScriptName = $ScriptName
    $script:Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $script:LogStartTime = Get-Date

    # Criar diretorio de logs se necessario
    if (-not $LogDir) {
        $LogDir = Join-Path $ProjectRoot "INSTALADOR\saida\logs"
    }
    if (-not (Test-Path $LogDir)) {
        New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    }

    # Nome do arquivo de log com timestamp
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $script:LogFile = Join-Path $LogDir "${ScriptName}_${timestamp}.log"

    # Iniciar log
    $header = @"
==========================================
BUILD LOG - $ScriptName
Inicio: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Host: $env:COMPUTERNAME
Usuario: $env:USERNAME
==========================================

"@
    $header | Set-Content -Path $script:LogFile -Encoding UTF8

    Write-Log INFO "Log iniciado: $script:LogFile"
}

function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet("INFO","STEP","OK","WARN","ERROR")]
        [string]$Level,
        [Parameter(Mandatory=$true)]
        [string]$Message
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $prefix = switch ($Level) {
        "INFO"  { "[INFO]" }
        "STEP"  { "[STEP]" }
        "OK"    { "[ OK ]" }
        "WARN"  { "[WARN]" }
        "ERROR" { "[ERRO]" }
    }
    $color = switch ($Level) {
        "INFO"  { "Gray" }
        "STEP"  { "Cyan" }
        "OK"    { "Green" }
        "WARN"  { "Yellow" }
        "ERROR" { "Red" }
    }

    $logLine = "[$timestamp] $prefix $Message"

    # Exibir no console com cor
    Write-Host $logLine -ForegroundColor $color

    # Gravar no arquivo de log
    if ($script:LogFile -and (Test-Path $script:LogFile)) {
        $logLine | Add-Content -Path $script:LogFile -Encoding UTF8
    }
}

function Start-BuildStep {
    param(
        [Parameter(Mandatory=$true)]
        [string]$StepName
    )

    $script:StepTimes[$StepName] = @{
        Start = [System.Diagnostics.Stopwatch]::StartNew()
        Duration = 0
    }
    Write-Log STEP "Iniciando: $StepName"
}

function Complete-BuildStep {
    param(
        [Parameter(Mandatory=$true)]
        [string]$StepName
    )

    if ($script:StepTimes.ContainsKey($StepName)) {
        $script:StepTimes[$StepName].Start.Stop()
        $duration = [math]::Round($script:StepTimes[$StepName].Start.Elapsed.TotalSeconds, 1)
        $script:StepTimes[$StepName].Duration = $duration
        Write-Log OK "$StepName concluido em ${duration}s"
        return $duration
    }
    return 0
}

function Get-StepDuration {
    param([string]$StepName)
    if ($script:StepTimes.ContainsKey($StepName)) {
        return $script:StepTimes[$StepName].Duration
    }
    return 0
}

function New-FileChecksum {
    param(
        [Parameter(Mandatory=$true)]
        [string]$FilePath
    )

    if (-not (Test-Path $FilePath)) {
        Write-Log WARN "Arquivo nao encontrado para checksum: $FilePath"
        return $null
    }

    try {
        $hash = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash.ToLower()
        $fileName = Split-Path -Leaf $FilePath
        $checksumFile = "${FilePath}.sha256"

        # Formato compativel com sha256sum
        "$hash  $fileName" | Set-Content -Path $checksumFile -Encoding ASCII

        Write-Log OK "Checksum SHA256 gerado: $checksumFile"
        Write-Log INFO "  $hash"
        return $hash
    } catch {
        Write-Log WARN "Erro ao gerar checksum: $_"
        return $null
    }
}

function Complete-BuildLog {
    param(
        [ValidateSet("SUCCESS","FAILED")]
        [string]$Status = "SUCCESS",
        [string]$Artifact,
        [string]$ArtifactSize,
        [string]$Checksum
    )

    $script:Stopwatch.Stop()
    $totalDuration = [math]::Round($script:Stopwatch.Elapsed.TotalSeconds, 1)

    Write-Host ""
    Write-Log INFO "=========================================="

    if ($Status -eq "SUCCESS") {
        Write-Log OK "BUILD CONCLUIDO - $script:LogScriptName"
    } else {
        Write-Log ERROR "BUILD FALHOU - $script:LogScriptName"
    }

    Write-Log INFO "=========================================="

    if ($Artifact) {
        Write-Log INFO "Artefato:  $Artifact"
    }
    if ($ArtifactSize) {
        Write-Log INFO "Tamanho:   $ArtifactSize"
    }
    if ($Checksum) {
        Write-Log INFO "SHA256:    $Checksum"
    }

    Write-Log INFO "------------------------------------------"
    Write-Log INFO "Duracoes:"

    foreach ($step in $script:StepTimes.Keys) {
        $duration = $script:StepTimes[$step].Duration
        Write-Log INFO "  ${step}: ${duration}s"
    }

    Write-Log INFO "  Total: ${totalDuration}s"
    Write-Log INFO "------------------------------------------"
    Write-Log INFO "Log: $script:LogFile"
    Write-Log INFO "=========================================="

    # Gravar resumo no arquivo de log
    if ($script:LogFile -and (Test-Path $script:LogFile)) {
        $summary = @"

==========================================
RESUMO FINAL
==========================================
Status: $Status
Duracao total: ${totalDuration}s
$(if ($Artifact) { "Artefato: $Artifact" })
$(if ($ArtifactSize) { "Tamanho: $ArtifactSize" })
$(if ($Checksum) { "SHA256: $Checksum" })
Fim: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
==========================================
"@
        $summary | Add-Content -Path $script:LogFile -Encoding UTF8
    }
}

function Add-DurationsToBuildInfo {
    param(
        [Parameter(Mandatory=$true)]
        [string]$BuildInfoPath
    )

    if (Test-Path $BuildInfoPath) {
        $durations = @()
        $durations += ""
        $durations += "# Duracoes (segundos)"

        foreach ($step in $script:StepTimes.Keys) {
            $duration = $script:StepTimes[$step].Duration
            $stepName = $step.ToUpper() -replace '\s+', '_'
            $durations += "DURATION_$stepName=$duration"
        }

        $totalDuration = [math]::Round($script:Stopwatch.Elapsed.TotalSeconds, 1)
        $durations += "DURATION_TOTAL=$totalDuration"

        $durations | Add-Content -Path $BuildInfoPath -Encoding UTF8
        Write-Log OK "Duracoes adicionadas a $BuildInfoPath"
    }
}

# Funcao para executar com tratamento de erro
function Invoke-BuildStep {
    param(
        [Parameter(Mandatory=$true)]
        [string]$StepName,
        [Parameter(Mandatory=$true)]
        [scriptblock]$ScriptBlock
    )

    Start-BuildStep -StepName $StepName
    try {
        & $ScriptBlock
        Complete-BuildStep -StepName $StepName
    } catch {
        Write-Log ERROR "Falha em ${StepName}: $_"
        Complete-BuildStep -StepName $StepName
        throw
    }
}
