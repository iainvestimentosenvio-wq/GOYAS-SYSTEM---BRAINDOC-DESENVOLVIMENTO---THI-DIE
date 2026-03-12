# Windows Scripts

Scripts
- `INSTALADOR/windows/scripts/build-msi.ps1`
- `INSTALADOR/windows/scripts/build-inno.ps1`
- `INSTALADOR/testes/windows/Test-Common.ps1`
- `INSTALADOR/testes/windows/test-msi.ps1`
- `INSTALADOR/testes/windows/test-inno.ps1`
- `INSTALADOR/testes/windows/test-upgrade-reinstall.ps1`
- `INSTALADOR/testes/windows/verify-artifacts.ps1`
- `INSTALADOR/testes/windows/run-regressao.ps1`
- `INSTALADOR/saida/test-logs/Run-E2E-Tests.ps1` (wrapper para regressao automatizada)

Pre-requisitos
- PowerShell 5.1+
- .NET SDK 8.x
- Inno Setup 6.2.2+ (para `build-inno.ps1`)
- WiX Toolset v4 (via `WixToolset.Sdk` para `build-msi.ps1`)

Execucao
```powershell
\INSTALADOR\windows\scripts\build-msi.ps1
\INSTALADOR\windows\scripts\build-inno.ps1
\INSTALADOR\testes\windows\run-regressao.ps1
```

Execucao parametrizada (sem caminho hardcoded)
```powershell
\INSTALADOR\testes\windows\run-regressao.ps1 `
  -ProjectRoot "C:\caminho\ProjetoProtons" `
  -Version "1.0.0" `
  -ArtifactsDir "C:\caminho\ProjetoProtons\INSTALADOR\saida\windows" `
  -LogDir "C:\caminho\ProjetoProtons\INSTALADOR\saida\test-logs"
```

Gate tecnico (performance)
- `run-regressao.ps1` aplica gate de performance por padrao:
  - MSI install p95 <= 90s
  - MSI uninstall p95 <= 45s
  - Inno install p95 <= 90s
  - Inno uninstall p95 <= 45s
- Se qualquer meta estourar, o veredito vai para `NO-GO` e o script retorna `exit code 3`.
- Override apenas para laboratorio: `-IgnorePerformanceGate`.

O que cada script faz
- `build-msi.ps1`: publish `Protons.UI`, gera WiX vars/proj, compila MSI, copia para `INSTALADOR/saida/windows`.
- `build-inno.ps1`: compila o instalador EXE via ISCC, aplica assinatura se configurado.
- `test-msi.ps1`: valida MSI (instalacao, atalhos, registro, AppData, uninstall e metrics).
- `test-inno.ps1`: valida Inno Setup com os IDs usados na matriz oficial.
- `test-upgrade-reinstall.ps1`: valida reinstall e preservacao de dados em `%APPDATA%`.
- `verify-artifacts.ps1`: valida hash e assinatura com estados separados (hash, assinatura e politica de falha).
- `run-regressao.ps1`: executa suite completa e gera `regressao-windows-<UTC>.json/.md`.

Variaveis de ambiente usadas
- `PROTONS_CERT_PATH` e `PROTONS_CERT_PASS` para assinatura.
- `SOURCE_DATE_EPOCH` e `TZ=UTC` para reprodutibilidade.

Saidas e logs
- Logs: `INSTALADOR/saida/logs`
- Metadata: `INSTALADOR/saida/metadata/build-info.txt`
- Artefatos: `INSTALADOR/saida/windows`

Zonas criticas
- `version.env`: se faltar, builds falham.
- `Init-Dirs.ps1`: precisa criar `saida/windows` e `saida/metadata`.
- `PROTONS_CERT_PATH`: assinatura muda hash do artefato.
