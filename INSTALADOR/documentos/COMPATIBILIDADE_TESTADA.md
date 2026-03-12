# COMPATIBILIDADE TESTADA

DataUTC: 2026-02-15T10:40:33Z
VersaoReferencia: 1.0.0
Regra: separar claramente "testado" de "nao testado".

## 1. Matriz factual (por evidência)

| Plataforma | Instalador | Estado | Evidencia |
| --- | --- | --- | --- |
| Windows 10 x64 | MSI | Em validacao (trilha Windows) | `saida/validacao-windows-20260215T000453Z/resumo.csv` |
| Windows 10 x64 | Inno EXE | Em validacao (trilha Windows) | `saida/validacao-windows-20260215T000453Z/resumo.csv` |
| Windows 11 x64 | MSI/Inno | Pendente da trilha Windows | `saida/validacao-windows-20260215T000453Z/gates-summary.md` |
| Linux x64 | AppImage | Validado | `saida/validacao-max-score-20260215T104033Z/13-test-linux-appimage.log` |
| Linux x64 | DEB | Parcial (conteudo com .pdb detectado; sudo restrito em rodada anterior) | `saida/validacao-max-score-20260215T104033Z/25-test-no-debug-symbols.log` |

## 2. Cobertura por cenário

| Cenario | Win10 | Win11 | Linux | Status global |
| --- | --- | --- | --- | --- |
| Install basico | Parcial | Pendente | Parcial | Parcial |
| Uninstall limpo | Pendente (6 FAIL historicos) | Pendente | Parcial | Pendente |
| Update local manifesto/hash | Pendente | Pendente | Validado local | Parcial |
| Assinatura corporativa | Bloqueado | Bloqueado | N/A | Bloqueado |
| Execucao em ambiente offline | Pendente | Pendente | Parcial | Pendente |

## 3. Edge cases (estado atual)

| Edge case | Estado |
| --- | --- |
| Windows ARM64 | Nao testado |
| Linux ARM64 | Nao testado |
| Windows x86 32-bit | Nao suportado nesta baseline |
| GPO restritiva (instalacao nao assinada) | Nao testado |
| Proxy corporativo para update | Nao testado |
| Defender/SmartScreen com assinatura final | Pendente (sem .pfx) |

## 4. O que mudou nesta rodada

- Revalidacao de hashes e pinning local de update em PASS.
- Isolamento de workflows do instalador validado em PASS.
- Detecção objetiva de `.pdb` dentro do pacote `.deb` (pendência aberta).

## 5. Evidencias principais

- `saida/validacao-max-score-20260215T104033Z/21-test-workflow-path-isolation.log`
- `saida/validacao-max-score-20260215T104033Z/22-test-release-hashes.log`
- `saida/validacao-max-score-20260215T104033Z/24-test-update-manifest-pinning.log`
- `saida/validacao-max-score-20260215T104033Z/25-test-no-debug-symbols.log`
- `saida/test-logs/e2e-results-20260207T174707.txt`
