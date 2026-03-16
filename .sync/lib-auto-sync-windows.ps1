Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DefaultLogRoot {
    if ($env:LOCALAPPDATA) {
        return (Join-Path $env:LOCALAPPDATA 'GOYAS-SYSTEMS-auto-sync')
    }

    return (Join-Path ([System.IO.Path]::GetTempPath()) 'GOYAS-SYSTEMS-auto-sync')
}

function Get-StatusPathFromLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Line
    )

    $path = $Line.Substring(3)
    if ($path -match ' -> ') {
        return ($path -replace '^.* -> ', '')
    }

    return $path
}

function Test-ShouldIgnorePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $normalized = $Path.Replace('\', '/')

    return (
        $normalized -match '(^|/)\.git/' -or
        $normalized -match '(^|/)\.sync/sync\.log$' -or
        $normalized -match '(^|/)\.sync/.*\.pid$' -or
        $normalized -match '(^|/)\.sync/runtime/' -or
        $normalized -match '(^|/)bin/' -or
        $normalized -match '(^|/)obj/' -or
        $normalized -match '(^|/)TestResults/' -or
        $normalized -match '\.trx$'
    )
}

function Get-RelevantPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo
    )

    git -C $Repo status --porcelain=v1 --untracked-files=all |
        ForEach-Object {
            $path = Get-StatusPathFromLine -Line $_
            if (-not (Test-ShouldIgnorePath -Path $path)) {
                $path
            }
        }
}

function Test-HasRelevantChanges {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo
    )

    return @(
        Get-RelevantPaths -Repo $Repo
    ).Count -gt 0
}

function New-AutoSyncState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo,
        [string]$ExpectedBranch,
        [string]$LogRoot
    )

    $repoRoot = (Resolve-Path $Repo).Path
    $branch = git -C $repoRoot branch --show-current
    if (-not $branch) {
        throw 'Nao foi possivel determinar a branch atual.'
    }

    if (-not $LogRoot) {
        $LogRoot = Get-DefaultLogRoot
    }

    $logDir = Join-Path $LogRoot $branch

    return [pscustomobject]@{
        Repo = $repoRoot
        Branch = $branch.Trim()
        ExpectedBranch = $ExpectedBranch
        LogRoot = $LogRoot
        LogDir = $logDir
        LogFile = Join-Path $logDir 'sync.log'
        PidFile = Join-Path $logDir 'auto-sync.pid'
        StartupCmdFile = Join-Path $logDir 'start-auto-sync.cmd'
    }
}

function Write-AutoSyncLog {
    param(
        [Parameter(Mandatory = $true)]
        $State,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    New-Item -ItemType Directory -Force -Path $State.LogDir | Out-Null
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$timestamp] $Message"
    Write-Host $line
    Add-Content -Path $State.LogFile -Value $line -Encoding UTF8
}

function Assert-ExpectedBranch {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    if ($State.ExpectedBranch -and $State.Branch -ne $State.ExpectedBranch) {
        throw "Branch atual '$($State.Branch)' difere da branch esperada '$($State.ExpectedBranch)'."
    }
}

function Test-RepositoryBlocked {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    $gitDir = git -C $State.Repo rev-parse --git-dir
    if (-not $gitDir) {
        return $false
    }

    $resolvedGitDir = if ([System.IO.Path]::IsPathRooted($gitDir.Trim())) {
        $gitDir.Trim()
    } else {
        Join-Path $State.Repo $gitDir.Trim()
    }

    if (Test-Path (Join-Path $resolvedGitDir 'rebase-merge')) { return $true }
    if (Test-Path (Join-Path $resolvedGitDir 'rebase-apply')) { return $true }
    if (Test-Path (Join-Path $resolvedGitDir 'MERGE_HEAD')) { return $true }

    $status = git -C $State.Repo status --porcelain
    foreach ($line in $status) {
        if ($line.StartsWith('UU') -or $line.StartsWith('AA') -or $line.StartsWith('DD')) {
            return $true
        }
    }

    return $false
}

function Ensure-Upstream {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    git -C $State.Repo rev-parse --abbrev-ref --symbolic-full-name '@{u}' *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    git -C $State.Repo ls-remote --exit-code --heads origin $State.Branch *> $null
    if ($LASTEXITCODE -eq 0) {
        git -C $State.Repo branch --set-upstream-to="origin/$($State.Branch)" $State.Branch *> $null
    }
}

function Invoke-SafePull {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    git -C $State.Repo ls-remote --exit-code --heads origin $State.Branch *> $null
    if ($LASTEXITCODE -ne 0) {
        return $true
    }

    if (Test-HasRelevantChanges -Repo $State.Repo) {
        Write-AutoSyncLog -State $State -Message 'Pull adiado: ha alteracoes locais relevantes.'
        return $false
    }

    if (Test-RepositoryBlocked -State $State) {
        Write-AutoSyncLog -State $State -Message 'Pull bloqueado: repositorio em conflito ou rebase/merge pendente.'
        return $false
    }

    git -C $State.Repo pull --rebase origin $State.Branch *>> $State.LogFile
    if ($LASTEXITCODE -ne 0) {
        Write-AutoSyncLog -State $State -Message 'Pull bloqueado: rebase/conflito exige resolucao manual na IDE.'
        return $false
    }

    return $true
}

function Invoke-PushChanges {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    if (-not (Test-HasRelevantChanges -Repo $State.Repo)) {
        return
    }

    if (Test-RepositoryBlocked -State $State) {
        Write-AutoSyncLog -State $State -Message 'Push bloqueado: repositorio em conflito ou rebase/merge pendente.'
        return
    }

    $paths = @(Get-RelevantPaths -Repo $State.Repo)
    if ($paths.Count -eq 0) {
        return
    }

    Write-AutoSyncLog -State $State -Message 'Alteracoes detectadas. Preparando commit e push.'
    git -C $State.Repo add -A -- @paths

    $message = "auto-sync: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [windows]"
    git -C $State.Repo commit -m $message *>> $State.LogFile
    if ($LASTEXITCODE -ne 0) {
        Write-AutoSyncLog -State $State -Message 'Commit nao criado. Nada novo para registrar ou ha bloqueio local.'
        return
    }

    Ensure-Upstream -State $State

    git -C $State.Repo ls-remote --exit-code --heads origin $State.Branch *> $null
    if ($LASTEXITCODE -eq 0) {
        if (-not (Invoke-SafePull -State $State)) {
            Write-AutoSyncLog -State $State -Message 'Push cancelado ate a resolucao do conflito.'
            return
        }

        git -C $State.Repo push origin $State.Branch *>> $State.LogFile
    } else {
        git -C $State.Repo push -u origin $State.Branch *>> $State.LogFile
    }

    if ($LASTEXITCODE -eq 0) {
        Write-AutoSyncLog -State $State -Message 'Push concluido com sucesso.'
    } else {
        Write-AutoSyncLog -State $State -Message 'Push falhou. Verifique o log.'
    }
}

function Invoke-PullIfBehind {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    git -C $State.Repo fetch origin *>> $State.LogFile
    $local = git -C $State.Repo rev-parse HEAD
    $remote = git -C $State.Repo rev-parse "origin/$($State.Branch)" 2>$null

    if ($LASTEXITCODE -ne 0 -or -not $remote) {
        return
    }

    if ($local.Trim() -ne $remote.Trim()) {
        Write-AutoSyncLog -State $State -Message 'Commits remotos detectados. Tentando pull seguro.'
        if (Invoke-SafePull -State $State) {
            Write-AutoSyncLog -State $State -Message 'Pull concluido.'
        } else {
            Write-AutoSyncLog -State $State -Message 'Pull nao aplicado automaticamente.'
        }
    }
}

function Get-ShellCommand {
    if ($IsWindows) {
        $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        if (Test-Path $powershell) {
            return $powershell
        }
    }

    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $powershellCmd = Get-Command powershell -ErrorAction SilentlyContinue
    if ($powershellCmd) {
        return $powershellCmd.Source
    }

    throw 'Nao foi possivel localizar PowerShell para iniciar o auto-sync.'
}
