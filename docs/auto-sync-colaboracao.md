# Auto-Sync de Colaboracao

Este repositorio passou a suportar o modo de branch compartilhada em `estabilizacao-fase1`, com o entendimento de que conflitos reais devem ser resolvidos manualmente na IDE.

## Estrategia oficial atual

- A branch compartilhada de operacao automatica e `estabilizacao-fase1`.
- O auto-sync pode rodar nas duas maquinas nessa mesma branch.
- O script deve parar o fluxo automatico quando encontrar conflito, rebase pendente ou estado inconsistente.
- A resolucao final continua sendo humana; o projeto nao tenta mesclar conflitos sozinho.

## Regras praticas

- Antes de ligar o auto-sync, a maquina precisa estar alinhada com `origin/estabilizacao-fase1`.
- Se houver conflito, resolva pela IDE e so depois religue o auto-sync.
- A branch de backup divergida deve ser preservada quando existir.
- Logs e PID do Windows ficam fora do repositorio, em `%LOCALAPPDATA%\GOYAS-SYSTEMS-auto-sync\<branch>`.

## Linux

- O script Linux ignora `.sync`, `.git`, `bin`, `obj`, `TestResults` e `.trx`.
- O log do Linux nao entra no commit automatico.
- O pull automatico e adiado quando ha alteracoes locais relevantes.

## Windows

- O script principal e [.sync/auto-sync-windows.ps1](/home/u/Documentos/GOYAS%20SYSTEMS/.sync/auto-sync-windows.ps1).
- Controle manual:
  - iniciar: [.sync/start-auto-sync-windows.ps1](/home/u/Documentos/GOYAS%20SYSTEMS/.sync/start-auto-sync-windows.ps1)
  - parar: [.sync/stop-auto-sync-windows.ps1](/home/u/Documentos/GOYAS%20SYSTEMS/.sync/stop-auto-sync-windows.ps1)
- Inicializacao automatica no boot:
  - instalar: [.sync/install-auto-sync-startup-windows.ps1](/home/u/Documentos/GOYAS%20SYSTEMS/.sync/install-auto-sync-startup-windows.ps1)

## Comandos principais no Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\.sync\start-auto-sync-windows.ps1 -ExpectedBranch estabilizacao-fase1
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\.sync\stop-auto-sync-windows.ps1 -ExpectedBranch estabilizacao-fase1
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\.sync\auto-sync-windows.ps1 -ExpectedBranch estabilizacao-fase1 -RunOnce
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\.sync\install-auto-sync-startup-windows.ps1 -ExpectedBranch estabilizacao-fase1
```

## Validacao minima antes de considerar pronto

- `git branch --show-current` deve retornar `estabilizacao-fase1`.
- `git rev-parse --abbrev-ref --symbolic-full-name "@{u}"` deve retornar `origin/estabilizacao-fase1`.
- `git status --short --branch` deve mostrar a arvore limpa e sem divergencia.
- O `RunOnce` do Windows deve completar sem erro.
- Start e stop manuais devem funcionar.
- O processo iniciado deve gravar PID e log fora do repositorio.
