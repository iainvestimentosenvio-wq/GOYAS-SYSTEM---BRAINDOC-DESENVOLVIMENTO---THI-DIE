# Comum

Escopo
Arquivos e scripts compartilhados entre Linux e Windows.

Versionamento
- Fonte unica: `INSTALADOR/comum/version.env`
- Props .NET: `INSTALADOR/comum/version.props`

Scripts comuns
- `INSTALADOR/comum/scripts/init-dirs.sh` e `Init-Dirs.ps1`
- `INSTALADOR/comum/scripts/log-utils.sh` e `Log-Utils.ps1`
- `INSTALADOR/comum/scripts/generate-sbom.sh` e `Generate-Sbom.ps1`
- `INSTALADOR/comum/scripts/generate-sbom-spdx.sh`
- `INSTALADOR/comum/scripts/validate-sbom.sh`
- `INSTALADOR/comum/scripts/collect-test-evidence.sh` e `Collect-Test-Evidence.ps1`
- `INSTALADOR/comum/scripts/validate-script-comments.sh`
- `INSTALADOR/comum/scripts/validate-prohibited-terms.sh`
- `INSTALADOR/comum/scripts/generate-update-manifest.sh`
- `INSTALADOR/comum/scripts/validate-update-manifest.sh`
- `INSTALADOR/comum/scripts/publish-update-manifest.sh`
- `INSTALADOR/comum/scripts/archive-update-manifest.sh`
- `INSTALADOR/comum/scripts/rollback-update-manifest.sh`
- `INSTALADOR/comum/scripts/check-update.sh`
- `INSTALADOR/comum/scripts/test-update-manifest.sh`

Scripts de validacao
- `validate-sbom.sh`: valida JSON do SBOM, componentes e versao.
- `collect-test-evidence.sh` e `Collect-Test-Evidence.ps1`: coleta logs, desktop entries e metadados de testes.
- `validate-script-comments.sh`: detecta comentarios potencialmente desatualizados em scripts.
- `validate-prohibited-terms.sh`: bloqueia termos proibidos nos documentos finais.
- `validate-update-manifest.sh`: valida contrato do manifesto de update.

Dependencias NuGet (runtime)
- `Avalonia` 11.3.11 (Protons.UI)
- `Avalonia.Desktop` 11.3.11 (Protons.UI)
- `Avalonia.Themes.Fluent` 11.3.11 (Protons.UI)
- `Avalonia.Fonts.Inter` 11.3.11 (Protons.UI)
- `Avalonia.Diagnostics` 11.3.11 (Protons.UI, debug)
- `CommunityToolkit.Mvvm` 8.2.1 (Protons.UI)
- `Microsoft.Data.Sqlite` 8.0.1 (Protons.Infrastructure)
- `Npgsql` 8.0.4 (Protons.Infrastructure)

SBOM
- Ferramentas: CycloneDX e Microsoft SBOM Tool (SPDX)
- Saida padrao: `INSTALADOR/saida/sbom/`
- SPDX: `generate-sbom-spdx.sh` usa `urn:protons:sbom` como namespace default (override via `SBOM_NAMESPACE_URI`).

Reprodutibilidade
- Variaveis usadas nos builds: `SOURCE_DATE_EPOCH`, `TZ=UTC`, `LC_ALL=C`, `LANG=C`
- Metadata: `INSTALADOR/saida/metadata/build-info.txt`

Diretorios de dados
Linux
- `~/.local/share/Protons` (ou `XDG_DATA_HOME/Protons` se definido)

Windows
- `%APPDATA%\Protons`

Nao usar
- Linux: `~/.config/Protons`
- Windows: `%LOCALAPPDATA%\Protons`

Zonas criticas
- `version.env`: fonte unica de versao, usada por todos os builds.
- Scripts comuns: erro aqui quebra Linux e Windows ao mesmo tempo.
