# RESILIENCIA TEST STRATEGY (UX + P10)

Data de inicio: 2026-02-18
Escopo: MSI + Inno (Win10/Win11)

## Baseline tecnico

- Gates obrigatorios: G1, G2, G3, G4.
- Runner base: `testes/windows/run-regressao.ps1`.
- Suite extra habilitada por flags:
  - `-EnableResilienceSuite`
  - `-EnableUxSuite`
  - `-RequireDefenderActive`

## Matriz de falhas controladas

1. MSI failpoint (`PROTONS_ROLLBACK_TEST=1`) -> rollback transacional esperado.
2. Inno failpoint (`/PROTONS_ROLLBACK_TEST=1`) -> cleanup custom esperado.
3. Interrupcao no meio (`/PROTONS_SLOW_INSTALL=1` + kill) -> estado final consistente.
4. Cancelamento simulado (`/PROTONS_CANCEL_TEST=1`) -> sem residuos.
5. Queda de energia simulada (kill + reinstall) -> recuperacao completa.
6. Repair MSI (`msiexec /fa`) -> arquivo recuperado.
7. Defender ativo -> install/uninstall de MSI e Inno em PASS.

## Evidencias obrigatorias

- `saida/ux-diaX/`
- `saida/ux-diaX/rollback/`
- `saida/test-logs/*-results-<run_id>.json`
- `saida/test-logs/*-metrics-<run_id>.json`

## Criterio de aceite da trilha P10

1. Suites `MSI-ROLLBACK-*` e `INNO-ROLLBACK-*` em PASS nas duas VMs.
2. Suites de interrupcao/recuperacao em PASS nas duas VMs.
3. Nenhuma regressao em G1-G4.

