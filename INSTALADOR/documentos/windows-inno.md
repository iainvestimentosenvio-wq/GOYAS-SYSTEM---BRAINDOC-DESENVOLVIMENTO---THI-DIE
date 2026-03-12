# Windows Inno Setup

Arquivo principal
- `INSTALADOR/windows/innosetup/protons-setup.iss`

Pre-requisitos
- Inno Setup 6.7.0+ (ISCC.exe) — obrigatorio para perfil dinamico light/dark do wizard
- Testado no ciclo atual com Inno Setup 6.7.0
- .NET SDK 8.x (publish realizado pelo build script)
- Icone: `INSTALADOR/windows/ativos/logo_protons.ico`
- SignTool (opcional, assinatura)

Build
```powershell
\INSTALADOR\windows\scripts\build-inno.ps1
```

Pipeline UX integrado no build:
- `build-inno.ps1` executa automaticamente `windows/scripts/prepare-inno-ux-assets.ps1`.
- O script gera:
  - `INSTALADOR/windows/ativos/installer/wizard_back_light.png`
  - `INSTALADOR/windows/ativos/installer/wizard_back_dark.png`
  - `INSTALADOR/windows/ativos/installer/wizard_small_logo_light.png`
  - `INSTALADOR/windows/ativos/installer/wizard_small_logo_dark.png`
- Se a geracao de assets falhar, o build do EXE e abortado.
- Se a versao do Inno for menor que 6.7.0, o build e abortado.

Artefato
- `INSTALADOR\saida\windows\ProtonsSetup-<VERSION>.exe`

Decisoes chave
- Wizard dinamico Windows 11 (light/dark automatico).
- Instalacao per-machine
- Atalhos para todos os usuarios
- Instalacao em `{autopf}\Protons`
- Metadados de instalacao em `HKLM\Software\Protons`
- Limpeza de legado `HKCU\Software\Protons` no uninstall
- JSONs empacotados por whitelist e `*.pdb` excluidos

Assinatura (opcional)
- Variaveis: `PROTONS_CERT_PATH` e `PROTONS_CERT_PASS`
- O build habilita assinatura no `.iss` quando o certificado existe

Testes
- Ver matriz consolidada em `documentos/MATRIZ_EVIDENCIAS_FINAL.md`
- Ver status executivo em `saida/RELATORIO-EXECUTIVO-FINAL.txt`

Zonas criticas
- `protons-setup.iss`: atalhos comuns e exclusao de `*.pdb`.
- `prepare-inno-ux-assets.ps1`: depende dos assets do Login (`login_bg.png` e `logo_protons.png`).
- `OutputDir`: precisa existir ou ser criado no build.
- Assinatura: variaveis e SignTool precisam estar configurados.
