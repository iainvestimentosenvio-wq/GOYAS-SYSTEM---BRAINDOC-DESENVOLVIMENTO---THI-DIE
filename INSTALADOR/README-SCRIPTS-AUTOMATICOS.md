# README - Scripts Automaticos Windows

Status operacional oficial:
- `saida/status/latest-status.json` (fonte unica de verdade)
- Derivados automaticos: `saida/status/latest-status.md`, `saida/status/latest-status.csv`

## Entrypoints semanticos (use estes)

### Gate base tecnico
```bash
bash run-windows-base-gate.sh
bash run-windows-base-gate.sh --help
```

### Gate P10 (resiliencia)
```bash
bash run-windows-p10-gate.sh
bash run-windows-p10-gate.sh --help
```

### Gate UX + P10
```bash
bash run-windows-ux-p10-gate.sh
bash run-windows-ux-p10-gate.sh --help
```

### Build EXE na VM + gate base
```bash
bash build-and-run-windows-gate.sh
bash build-and-run-windows-gate.sh --help
```

### Canary curto (pre-gate longo)
```bash
bash run-windows-canary.sh
bash run-windows-canary.sh --help
```

Comportamento operacional:
- executa do Linux sem passos manuais na VM para build
- rebuild do EXE ocorre automaticamente na VM
- rebuild do MSI ocorre automaticamente na VM quando stale
- requer `dotnet` no host Linux para preparar publish win-x64 do bundle

### Diagnostico de upload/transferencia
```bash
bash troubleshoot-upload.sh
```

Semantica das opcoes do troubleshooter:
- `1`: ajusta QGA e sai (nao inicia HTTP)
- `2`: inicia fallback HTTP local
- `3`: ajusta QGA e prepara fallback HTTP sem iniciar servidor

## Compatibilidade (wrappers legados)

Os comandos abaixo continuam funcionando, mas estao deprecados e redirecionam para os entrypoints semanticos:
- `RODAR-FINAL2-CORRIGIDO.sh`
- `RODAR-P10-GATE.sh`
- `RODAR-UX-P10-GATE.sh`
- `RODAR-BUILD-E-VALIDA.sh`
- `comum/scripts/FIX-UPLOAD-RAPIDO.sh`

## Politica de assinatura

- Perfil tecnico (default): `technical`
  - sem signtool/certificado pode gerar `WARN`
- Perfil de producao: `production`
  - ausencia de assinatura valida bloqueia (`FAIL`)

Exemplo para forcar perfil de producao:
```bash
PROTONS_SIGNATURE_PROFILE=production bash run-windows-base-gate.sh
```

## Preflight obrigatorio

Todos os gates executam preflight de artefato:
```bash
bash testes/windows/test-artifact-freshness.sh
```

Se falhar, rebuild obrigatorio:
```bash
if command -v pwsh >/dev/null 2>&1; then PS_CMD=pwsh; else PS_CMD=powershell; fi
$PS_CMD -ExecutionPolicy Bypass -File windows/scripts/build-msi.ps1
$PS_CMD -ExecutionPolicy Bypass -File windows/scripts/build-inno.ps1

# Fluxo automatico Linux->VM (recomendado)
bash build-and-run-windows-gate.sh
```

## Evidencias para validacao

- Rodada: `saida/validacao-windows-<RUN_ID>/`
- Gates: `saida/validacao-windows-<RUN_ID>/gates-summary.md`
- CSV de passos: `saida/validacao-windows-<RUN_ID>/resumo.csv`
- Status oficial consolidado: `saida/status/latest-status.json`

## Portabilidade

Scripts ativos nao dependem mais de caminho absoluto fixo.
Se necessario, ajuste por env:
- `ISO_SEARCH_DIRS`
- `PROTONS_SHARED_ROOT`
- `PROTONS_SIGNATURE_PROFILE`
- `QGA_PAYLOAD_TRANSPORT_MODE` (`iso-strict` padrao, `iso`, `qga`, `auto`)
- `PROTONS_SKIP_CANARY` (`0` padrao, `1` para bypass emergencial)

Transporte de payload:
- `iso-strict`: ISO obrigatorio (falha imediata sem fallback)
- `iso`/`auto`: ISO primario com fallback QGA/HTTP
- `qga`: QGA direto
