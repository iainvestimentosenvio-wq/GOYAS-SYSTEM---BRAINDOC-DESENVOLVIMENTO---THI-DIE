# Windows WiX (MSI)

Arquivos principais
- `INSTALADOR/windows/wix/Product.wxs`
- `INSTALADOR/windows/wix/Components.wxs`
- `INSTALADOR/windows/wix/Features.wxs`
- `INSTALADOR/windows/wix/Variables.wxi`
- `INSTALADOR/windows/wix/Protons.wixproj`

Pre-requisitos
- .NET SDK 8.x (build e publish)
- WiX Toolset v4 via `WixToolset.Sdk` (no `Protons.wixproj`)
- Windows SDK para `signtool.exe` (opcional, assinatura)
- Icone: `INSTALADOR/windows/ativos/logo_protons.ico`

Build
```powershell
\INSTALADOR\windows\scripts\build-msi.ps1
```

Artefato
- `INSTALADOR\saida\windows\Protons-<VERSION>-x64.msi`

Fluxo do build (resumo)
- Faz `dotnet publish` do `Login/Protons.UI` em `win-x64`.
- Gera `Variables.wxi` e `Protons.wixproj` com versao centralizada.
- Compila WiX com `dotnet build`.
- Copia o MSI para `INSTALADOR/saida/windows`.
- Assina com SignTool se `PROTONS_CERT_PATH` estiver definido.

Decisoes chave
- Instalacao per-machine
- Dados em `%APPDATA%\Protons`
- Instalacao em `Program Files` (64-bit)
- Atalhos em Desktop comum e Menu Iniciar comum
- Registro de metadados de instalacao em `HKLM\Software\Protons`
- Limpeza de legado `HKCU\Software\Protons` no uninstall para compatibilidade

Comportamento do instalador
- `Product.wxs` define `InstallScope="perMachine"` e `InstallPrivileges="elevated"`.
- `Components.wxs` cria `AppDataFolder\Protons`, grava metadados per-machine em `HKLM` e remove legado `HKCU` no uninstall.
- JSONs empacotados por whitelist e `*.pdb` excluidos.
- `Assets\**` apenas se existir no publish.

Testes
- Ver matriz consolidada em `documentos/MATRIZ_EVIDENCIAS_FINAL.md`
- Ver status executivo em `saida/RELATORIO-EXECUTIVO-FINAL.txt`

Zonas criticas
- `Product.wxs`: escopo per-machine e AppData.
- `Components.wxs`: atalhos e whitelist JSON.
- `Variables.wxi`: caminho de publish e versao.
