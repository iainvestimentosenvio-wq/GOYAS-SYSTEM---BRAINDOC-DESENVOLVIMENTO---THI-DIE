# Bootstrap automatico do QEMU Guest Agent dentro do Windows.
# Este script deve ser executado como administrador.

[CmdletBinding()]
param(
    [switch]$ElevatedRelaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$baseDir = 'C:\ProgramData\Protons'
$logPath = Join-Path $baseDir 'qga-bootstrap.log'
$sentinelPath = Join-Path $baseDir 'automation-ready.json'

if (-not (Test-Path $baseDir)) {
    New-Item -ItemType Directory -Path $baseDir -Force | Out-Null
}

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f ([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')), $Message
    Add-Content -Path $logPath -Value $line
    Write-Host $line
}

function Find-Installer {
    $candidates = @()
    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        $root = $drive.Root.TrimEnd('\\')
        $candidates += (Join-Path $root 'qemu-ga-x86_64.msi')
        $candidates += (Join-Path $root 'guest-agent\qemu-ga-x86_64.msi')
        $candidates += (Join-Path $root 'a.exe')
        $candidates += (Join-Path $root 'guest-tools\a.exe')
    }

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $null
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Admin {
    if (Test-IsAdmin) {
        return
    }

    if ($ElevatedRelaunch) {
        throw 'Nao foi possivel concluir a elevacao administrativa (UAC recusado ou bloqueado).'
    }

    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw 'Nao foi possivel determinar o caminho do script para relancamento elevado.'
    }

    Write-Log 'Sessao sem privilegio administrativo; solicitando elevacao UAC.'
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-ElevatedRelaunch'
    )

    $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $args -Wait -PassThru
    if ($null -eq $proc) {
        throw 'Falha ao iniciar processo elevado do bootstrap.'
    }
    exit $proc.ExitCode
}

try {
    Ensure-Admin
    Write-Log 'Inicio do bootstrap do QEMU Guest Agent.'

    $installer = Find-Installer
    if (-not $installer) {
        throw 'Nenhum instalador de guest agent encontrado nos drives montados.'
    }

    Write-Log ("Instalador localizado: {0}" -f $installer)

    if ($installer.ToLower().EndsWith('.msi')) {
        $args = @('/i', $installer, '/qn', '/norestart')
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $args -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw ("Falha no msiexec: exit code {0}" -f $proc.ExitCode)
        }
        Write-Log 'Instalacao MSI concluida com sucesso.'
    } else {
        $proc = Start-Process -FilePath $installer -ArgumentList @('/S') -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw ("Falha no instalador EXE: exit code {0}" -f $proc.ExitCode)
        }
        Write-Log 'Instalacao EXE concluida com sucesso.'
    }

    $serviceName = 'qemu-ga'
    $service = Get-Service -Name $serviceName -ErrorAction Stop

    if ($service.StartType -ne 'Automatic') {
        Set-Service -Name $serviceName -StartupType Automatic
        Write-Log 'StartupType do qemu-ga ajustado para Automatic.'
    }

    if ($service.Status -ne 'Running') {
        Start-Service -Name $serviceName
        Write-Log 'Servico qemu-ga iniciado.'
    }

    Start-Sleep -Seconds 3
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($service.Status -ne 'Running') {
        throw 'Servico qemu-ga nao ficou em estado Running.'
    }

    $payload = [ordered]@{
        timestamp_utc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        status = 'READY'
        service = $serviceName
        installer = $installer
        host = $env:COMPUTERNAME
    }
    $payload | ConvertTo-Json -Depth 4 | Set-Content -Path $sentinelPath -Encoding UTF8

    Write-Log ("Sentinela gravada em: {0}" -f $sentinelPath)
    Write-Log 'Bootstrap concluido com sucesso.'
    exit 0
} catch {
    Write-Log ("ERRO: {0}" -f $_.Exception.Message)
    try {
        $payload = [ordered]@{
            timestamp_utc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
            status = 'ERROR'
            error = $_.Exception.Message
        }
        $payload | ConvertTo-Json -Depth 4 | Set-Content -Path $sentinelPath -Encoding UTF8
    } catch {
    }
    exit 1
}
