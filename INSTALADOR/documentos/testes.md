# Testes do Instalador

Objetivo
Manter o checklist unico de testes e as evidencias em um unico lugar.

Regra de execucao em 2 momentos (obrigatoria)
- `AGORA (execucao continua)`: executar testes de contrato/seguranca e validacoes anti-falso-positivo durante o desenvolvimento.
- `FINAL (fechamento da release)`: repetir os gates em artefatos finais assinados e registrar veredito formal de `GO/NO-GO`.
- Regra de interpretacao: testes de seguranca/governanca nao podem ser deixados apenas para o fechamento.

Linux
- AppImage: `bash INSTALADOR/testes/linux/test-appimage.sh`
- DEB: `bash INSTALADOR/testes/linux/test-deb.sh`

Windows
- MSI: `\INSTALADOR\windows\scripts\build-msi.ps1` e `\INSTALADOR\testes\windows\test-msi.ps1`
- Inno: `\INSTALADOR\windows\scripts\build-inno.ps1` e `\INSTALADOR\testes\windows\test-inno.ps1`
- Upgrade/Reinstall: `\INSTALADOR\testes\windows\test-upgrade-reinstall.ps1`
- Regressao unificada: `\INSTALADOR\testes\windows\run-regressao.ps1`
- Hash/assinatura: `\INSTALADOR\testes\windows\verify-artifacts.ps1`
- Saida da regressao: `regressao-windows-<UTC>.json` e `regressao-windows-<UTC>.md`

O que os testes validam
- AppImage: estrutura do AppDir, `AppRun`, `.desktop` e execucao.
- DEB: conteudo do pacote, instalacao, execucao e desinstalacao.
- MSI/Inno: instalacao, atalhos, registro, AppData, uninstall e geracao de metrics.

Scripts de validacao adicionais
- `INSTALADOR/comum/scripts/validate-sbom.sh`: valida SBOM em CI/local.
- `INSTALADOR/comum/scripts/collect-test-evidence.sh`: coleta logs, desktop entries e metadados.
- `INSTALADOR/comum/scripts/validate-script-comments.sh`: valida comentarios desatualizados em scripts.
- `INSTALADOR/comum/scripts/validate-prohibited-terms.sh`: valida termos proibidos na doc final.
- `INSTALADOR/comum/scripts/test-update-manifest.sh`: valida manifesto, assinatura e fluxo estrito de update.

Gate de regressao Windows
- Veredito tecnico: `GO_TECNICO_WINDOWS` ou `NO-GO`.
- Bloqueios obrigatorios por padrao:
  - qualquer `FAIL` na suite;
  - itens `MANUAL` sem `-AllowManual`;
  - p95 acima da meta (MSI install<=90, MSI uninstall<=45, Inno install<=90, Inno uninstall<=45).
- Override de laboratorio disponivel: `-IgnorePerformanceGate`.

Contrato de update testado automaticamente
- `channel` invalido falha.
- `published_at_utc` invalido falha.
- URL relativa de artefato ou assinatura falha.
- `UNSIGNED` em modo estrito falha.
- Assinatura valida com chave publica passa em `check-update.sh --require-signature`.

Evidencias
- Linux: `bash INSTALADOR/comum/scripts/collect-test-evidence.sh`
- Windows: `\INSTALADOR\comum\scripts\Collect-Test-Evidence.ps1`

Checklist detalhado
- Fonte oficial de resultado: `documentos/MATRIZ_EVIDENCIAS_FINAL.md`
- Documento mestre de status: `saida/RELATORIO-EXECUTIVO-FINAL.txt`

Zonas criticas
- Rodar testes em ambiente limpo (evita falso positivo).
- Windows precisa validacao real em VM.
