# Correcao Real para Auto-Sync Compartilhado

Este documento registra apenas o que precisou ser corrigido no projeto para suportar auto-sync compartilhado em `estabilizacao-fase1`.

## O que estava faltando

- A documentacao oficial ainda falava em branches pessoais como fluxo recomendado.
- O script do Windows ainda usava log dentro do repositorio.
- O script do Windows nao tinha bootstrap de upstream nem criacao de branch remota.
- O script do Windows fazia `pull --rebase` sem bloquear em alteracao local relevante.
- O projeto nao tinha scripts claros de start/stop no Windows.
- O projeto nao tinha um instalador de inicializacao automatica no boot do Windows.

## O que foi corrigido

- O Windows agora usa log e PID fora do repositorio.
- O Windows passou a ignorar `.git`, `.sync`, `bin`, `obj`, `TestResults` e `*.trx`.
- O Windows valida a branch esperada antes de iniciar.
- O Windows tenta configurar upstream quando a branch remota ja existe.
- O Windows cria a branch remota com `git push -u origin <branch>` quando necessario.
- O Windows adia pull automatico quando ha alteracoes locais relevantes.
- O Windows bloqueia push/pull automatico quando detecta conflito, merge ou rebase pendente.
- Foram adicionados scripts de start, stop e instalacao no boot.
- A documentacao passou a refletir a operacao em `estabilizacao-fase1`.

## O que ainda depende da operacao

- Cada maquina precisa estar alinhada com `origin/estabilizacao-fase1` antes de ligar o auto-sync.
- Conflitos reais continuam sendo resolvidos manualmente na IDE.
- Branches de backup divergidas nao devem ser apagadas sem revisao humana.
