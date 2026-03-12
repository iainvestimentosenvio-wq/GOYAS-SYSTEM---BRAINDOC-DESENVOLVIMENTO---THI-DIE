# REQUISITOS MINIMOS

DataUTC: 2026-02-15T10:40:33Z
VersaoReferencia: 1.0.0
Politica: documento factual, separado por "testado" e "nao testado".

## 1. Requisitos de sistema

### 1.1 Windows (alvo)

| Item | Minimo | Recomendado | Status factual |
| --- | --- | --- | --- |
| SO | Windows 10 x64 / Windows 11 x64 | Windows 11 x64 atualizado | Em validacao por equipe Windows |
| CPU | 2 nucleos | 4 nucleos | Parcial (infra de VM validada, regressao in-guest pendente) |
| RAM | 4 GB | 8 GB | Parcial |
| Disco livre | 2 GB | 5 GB | Parcial |
| Privilegios | Administrador para install/uninstall | Admin + politica corporativa alinhada | Confirmado no fluxo Windows |

Notas:
- Build numbers especificos de Windows seguem pendentes de consolidacao final da trilha Win10/Win11.
- Sem fechamento de G1/G2/G3/G4, este bloco nao pode ser marcado como totalmente validado.

### 1.2 Linux (alvo)

| Item | Minimo | Recomendado | Status factual |
| --- | --- | --- | --- |
| SO DEB | Ubuntu/Debian x64 | Ubuntu LTS recente | Parcial (sudo bloqueado em parte do teste DEB) |
| SO AppImage | Linux x64 com suporte a AppImage | distro x64 recente | Validado (`test-appimage.sh` PASS) |
| CPU | 2 nucleos | 4 nucleos | Validado localmente |
| RAM | 4 GB | 8 GB | Validado localmente |
| Disco livre | 2 GB | 5 GB | Validado localmente |

Dependencias praticas Linux:
- `bash`, `jq`, `openssl`, `sha256sum` para validadores e testes.
- Para inspeção de `.deb`: `dpkg-deb` (usado em `test-no-debug-symbols.sh`).
- Para AppImage em alguns ambientes: FUSE pode ser necessario.

## 2. Rede e update

| Cenario | Requisito | Status factual |
| --- | --- | --- |
| Instalar artefato local (MSI/EXE/AppImage/DEB) | Pode ocorrer sem internet | Confirmado |
| Fluxo de update por manifesto | Acesso ao endpoint de manifesto e artefato | Parcial (validado local via file:// e checks; ciclo HTTP real pendente) |
| Proxy corporativo | Configuracao de proxy no host/app | Nao testado |
| Firewall corporativo | Liberacao dos endpoints de update | Nao testado |

## 3. Seguranca e assinatura

| Item | Exigencia | Status factual |
| --- | --- | --- |
| Assinatura de codigo Windows | Certificado corporativo `.pfx` + Authenticode | Bloqueado (certificado indisponivel) |
| Timestamp RFC3161 | Obrigatorio para assinatura final | Processo documentado, execucao pendente |
| Validacao de hashes | SHA-256 por artefato | Validado (`test-release-hashes.sh` PASS) |
| Pinning/validacao de assinatura no update | Verificacao por chave publica em modo estrito | Validado localmente (`test-update-manifest-pinning.sh`) |

## 4. Ambientes e politicas corporativas

| Item | Estado |
| --- | --- |
| Defender/SmartScreen com artefato assinado corporativamente | Pendente (depende de certificado e trilha Windows) |
| GPO bloqueando software nao assinado | Nao testado |
| ARM64 | Nao testado |
| x86 32-bit | Nao suportado nesta baseline |

## 5. Evidencias usadas neste documento

- `saida/validacao-max-score-20260215T104033Z/22-test-release-hashes.log`
- `saida/validacao-max-score-20260215T104033Z/24-test-update-manifest-pinning.log`
- `saida/validacao-max-score-20260215T104033Z/25-test-no-debug-symbols.log`
- `saida/validacao-paralela-20260215T101334Z/resumo.csv`
- `saida/validacao-windows-20260215T000453Z/gates-summary.md`
