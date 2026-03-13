# Auto-sync Windows: monitora alterações e faz push/pull automático
# Execute como: powershell -ExecutionPolicy Bypass -File auto-sync-windows.ps1

$REPO = "C:\GOYAS-SYSTEMS"   # <-- ALTERE para o caminho onde clonou o repositório
$LOG  = "$REPO\.sync\sync.log"
$DEBOUNCE = 5000  # milissegundos

function Write-Log($msg) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $msg"
    Write-Host $line
    Add-Content -Path $LOG -Value $line -Encoding UTF8
}

function Push-Changes {
    Set-Location $REPO

    $status = git status --porcelain
    if (-not $status) { return }

    Write-Log "Alterações detectadas — fazendo commit e push..."
    git add -A
    $mensagem = "auto-sync: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [windows]"
    git commit -m $mensagem >> $LOG 2>&1

    $branch = git branch --show-current
    git pull --rebase origin $branch >> $LOG 2>&1
    git push origin $branch >> $LOG 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Log "Push concluído com sucesso"
    } else {
        Write-Log "ERRO no push — verifique o log"
    }
}

function Pull-IfBehind {
    Set-Location $REPO
    git fetch origin >> $LOG 2>&1
    $branch = git branch --show-current
    $local  = git rev-parse HEAD
    $remote = git rev-parse "origin/$branch" 2>$null

    if ($local -ne $remote -and $remote) {
        Write-Log "Commits novos detectados no GitHub — fazendo pull..."
        git pull --rebase origin $branch >> $LOG 2>&1
        Write-Log "Pull concluído"
    }
}

# Garante que o diretório de log existe
New-Item -ItemType Directory -Force -Path "$REPO\.sync" | Out-Null

Write-Log "=== Auto-sync iniciado (Windows) ==="
Write-Log "Monitorando: $REPO"

# Configura o FileSystemWatcher
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $REPO
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true
$watcher.Filter = "*"

$global:pendingChange = $false
$global:lastChange = [DateTime]::MinValue

$action = {
    $path = $Event.SourceEventArgs.FullPath
    # Ignora .git, .sync, bin, obj
    if ($path -match '\\\.git\\|\\\.sync\\|\\bin\\|\\obj\\|\.trx$|TestResults') { return }
    $global:pendingChange = $true
    $global:lastChange = Get-Date
}

Register-ObjectEvent $watcher "Changed" -Action $action | Out-Null
Register-ObjectEvent $watcher "Created" -Action $action | Out-Null
Register-ObjectEvent $watcher "Deleted" -Action $action | Out-Null
Register-ObjectEvent $watcher "Renamed" -Action $action | Out-Null

$pullTimer = [DateTime]::Now

Write-Log "Monitoramento ativo. Pressione Ctrl+C para parar."

while ($true) {
    Start-Sleep -Milliseconds 1000

    # Verifica debounce para push
    if ($global:pendingChange) {
        $elapsed = ([DateTime]::Now - $global:lastChange).TotalMilliseconds
        if ($elapsed -ge $DEBOUNCE) {
            $global:pendingChange = $false
            Push-Changes
        }
    }

    # Pull periódico a cada 30 segundos
    $sinceLastPull = ([DateTime]::Now - $pullTimer).TotalSeconds
    if ($sinceLastPull -ge 30) {
        $pullTimer = [DateTime]::Now
        Pull-IfBehind
    }
}
