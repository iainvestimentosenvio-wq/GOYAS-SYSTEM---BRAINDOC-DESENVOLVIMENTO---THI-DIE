param(
    [string]$ExpectedBranch = "colega-dev",
    [int]$DebounceMs = 5000,
    [int]$PullIntervalSeconds = 30,
    [switch]$RunOnce
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$StateDirName = "GOYAS-SYSTEMS-auto-sync"
$StateRoot = if ($env:LOCALAPPDATA) {
    Join-Path $env:LOCALAPPDATA $StateDirName
} else {
    Join-Path $env:TEMP $StateDirName
}
$StateDir = Join-Path $StateRoot $ExpectedBranch
$Log = Join-Path $StateDir "sync.log"
$script:LastBranchWarning = ""

function Write-Log([string]$Message) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $Message"
    Write-Host $line
    Add-Content -Path $Log -Value $line -Encoding utf8
}

function Get-CurrentBranch {
    return ((git branch --show-current) | Out-String).Trim()
}

function Test-OnExpectedBranch {
    $branch = Get-CurrentBranch
    if ($branch -eq $ExpectedBranch) {
        $script:LastBranchWarning = ""
        return $true
    }

    $message = "Branch atual '$branch' nao eh '$ExpectedBranch'; auto-sync pausado"
    if ($script:LastBranchWarning -ne $message) {
        Write-Log $message
        $script:LastBranchWarning = $message
    }
    return $false
}

function Get-StatusPathFromPorcelain([string]$Line) {
    if ([string]::IsNullOrWhiteSpace($Line) -or $Line.Length -le 3) {
        return ""
    }

    $path = $Line.Substring(3)
    if ($path -like "* -> *") {
        $path = ($path -split " -> ", 2)[1]
    }

    return $path.Trim()
}

function Should-IgnorePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $true
    }

    $normalized = $Path.Replace("\", "/")

    if ($normalized -like ".git/*" -or $normalized -like "*/.git/*") { return $true }
    if ($normalized -like ".sync/*" -or $normalized -like "*/.sync/*") { return $true }
    if ($normalized -like "bin/*" -or $normalized -like "*/bin/*") { return $true }
    if ($normalized -like "obj/*" -or $normalized -like "*/obj/*") { return $true }
    if ($normalized -like "TestResults/*" -or $normalized -like "*/TestResults/*") { return $true }
    if ($normalized -like "*.trx") { return $true }

    return $false
}

function Get-RelevantChanges {
    $changes = New-Object System.Collections.Generic.List[string]
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $lines = @(git status --porcelain=v1 --untracked-files=all)

    foreach ($line in $lines) {
        $path = Get-StatusPathFromPorcelain $line
        if (Should-IgnorePath $path) {
            continue
        }

        if ($seen.Add($path)) {
            [void]$changes.Add($path)
        }
    }

    return ,$changes.ToArray()
}

function Test-HasRelevantChanges {
    $changes = @(Get-RelevantChanges)
    return $changes.Count -gt 0
}

function Stage-RelevantChanges {
    $paths = @(Get-RelevantChanges)
    if ($paths.Count -eq 0) {
        return $false
    }

    git add -A -- $paths *>> $Log
    if ($LASTEXITCODE -ne 0) {
        Write-Log "ERRO no git add das alteracoes relevantes; verifique o log"
        return $false
    }

    return $true
}

function Test-RemoteBranchExists([string]$Branch) {
    git show-ref --verify --quiet "refs/remotes/origin/$Branch"
    return $LASTEXITCODE -eq 0
}

function Ensure-Upstream {
    if (-not (Test-OnExpectedBranch)) {
        return $false
    }

    $desired = "origin/$ExpectedBranch"
    $upstream = ((git rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>$null) | Out-String).Trim()
    if ($upstream -eq $desired) {
        return $true
    }

    if (-not (Test-RemoteBranchExists $ExpectedBranch)) {
        return $false
    }

    git branch --set-upstream-to=$desired $ExpectedBranch *>> $Log
    if ($LASTEXITCODE -ne 0) {
        Write-Log "ERRO ao configurar upstream para $desired; verifique o log"
        return $false
    }

    Write-Log "Upstream ajustado para $desired"
    return $true
}

function Pull-BranchIfClean {
    if (-not (Test-OnExpectedBranch)) {
        return $false
    }

    if (-not (Test-RemoteBranchExists $ExpectedBranch)) {
        return $true
    }

    if (Test-HasRelevantChanges) {
        Write-Log "Pull adiado: ha alteracoes locais relevantes"
        return $false
    }

    git pull --rebase origin $ExpectedBranch *>> $Log
    if ($LASTEXITCODE -ne 0) {
        Write-Log "ERRO no pull --rebase; verifique o log"
        return $false
    }

    return $true
}

function Push-Changes {
    Set-Location $RepoRoot

    if (-not (Test-OnExpectedBranch)) {
        return
    }

    if (-not (Test-HasRelevantChanges)) {
        return
    }

    Write-Log "Alteracoes detectadas; fazendo commit e push..."

    if (-not (Stage-RelevantChanges)) {
        if (-not (Test-HasRelevantChanges)) {
            Write-Log "Nenhuma alteracao relevante para commit"
        }
        return
    }

    $message = "auto-sync: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [windows]"
    git commit -m $message *>> $Log
    if ($LASTEXITCODE -ne 0) {
        Write-Log "Nenhum commit novo foi criado ou houve falha no commit; verifique o log"
        return
    }

    if (Test-RemoteBranchExists $ExpectedBranch) {
        if (-not (Ensure-Upstream)) {
            return
        }

        if (-not (Pull-BranchIfClean)) {
            Write-Log "Push cancelado porque o pull nao ficou seguro"
            return
        }

        git push origin $ExpectedBranch *>> $Log
    } else {
        git push -u origin $ExpectedBranch *>> $Log
    }

    if ($LASTEXITCODE -eq 0) {
        Write-Log "Push concluido com sucesso"
    } else {
        Write-Log "ERRO no push; verifique o log"
    }
}

function Pull-IfBehind {
    Set-Location $RepoRoot

    if (-not (Test-OnExpectedBranch)) {
        return
    }

    git fetch origin *>> $Log
    if ($LASTEXITCODE -ne 0) {
        Write-Log "ERRO no fetch; verifique o log"
        return
    }

    if (-not (Test-RemoteBranchExists $ExpectedBranch)) {
        return
    }

    if (-not (Ensure-Upstream)) {
        return
    }

    $local = ((git rev-parse HEAD) | Out-String).Trim()
    $remote = ((git rev-parse "origin/$ExpectedBranch" 2>$null) | Out-String).Trim()

    if ($local -ne $remote -and $remote) {
        Write-Log "Commits novos detectados no GitHub; fazendo pull..."
        if (Pull-BranchIfClean) {
            Write-Log "Pull concluido"
        } else {
            Write-Log "Pull nao aplicado automaticamente"
        }
    }
}

function Start-WatchLoop {
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = $RepoRoot
    $watcher.IncludeSubdirectories = $true
    $watcher.EnableRaisingEvents = $true
    $watcher.Filter = "*"

    $global:PendingChange = $false
    $global:LastChange = [DateTime]::MinValue

    $action = {
        $path = $Event.SourceEventArgs.FullPath
        if (Should-IgnorePath $path) {
            return
        }

        $global:PendingChange = $true
        $global:LastChange = Get-Date
    }

    Register-ObjectEvent $watcher "Changed" -Action $action | Out-Null
    Register-ObjectEvent $watcher "Created" -Action $action | Out-Null
    Register-ObjectEvent $watcher "Deleted" -Action $action | Out-Null
    Register-ObjectEvent $watcher "Renamed" -Action $action | Out-Null

    try {
        Write-Log "Monitoramento ativo. Pressione Ctrl+C para parar."
        $pullTimer = [DateTime]::Now

        while ($true) {
            Start-Sleep -Milliseconds 1000

            if ($global:PendingChange) {
                $elapsed = ([DateTime]::Now - $global:LastChange).TotalMilliseconds
                if ($elapsed -ge $DebounceMs) {
                    $global:PendingChange = $false
                    Push-Changes
                }
            }

            $sinceLastPull = ([DateTime]::Now - $pullTimer).TotalSeconds
            if ($sinceLastPull -ge $PullIntervalSeconds) {
                $pullTimer = [DateTime]::Now
                Pull-IfBehind
            }
        }
    }
    finally {
        Get-EventSubscriber | Where-Object { $_.SourceObject -eq $watcher } | Unregister-Event -Force
        $watcher.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $StateDir | Out-Null

Write-Log "=== Auto-sync iniciado (Windows) ==="
Write-Log "Repositorio: $RepoRoot"
Write-Log "Branch monitorada: $ExpectedBranch"
Write-Log "Log local: $Log"

if ($RunOnce) {
    Pull-IfBehind
    Push-Changes
    exit 0
}

Pull-IfBehind
Start-WatchLoop
