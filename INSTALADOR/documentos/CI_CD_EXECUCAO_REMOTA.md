# CI/CD EXECUCAO REMOTA

DataUTC: 2026-02-15T10:40:33Z
Escopo: handoff para equipe com acesso GitHub (PR/tag real).

## Contexto local desta rodada

- Workflows válidos localmente:
  - `.github/workflows/instalador-ci.yml`
  - `.github/workflows/instalador-release.yml`
- Limitação deste ambiente:
  - sem `gh`
  - sem `act`
  - sem remoto Git configurado

## Objetivo do handoff

Executar workflows no GitHub Actions sem alterar escopo técnico desta rodada.

## Passo a passo (equipe remota)

1. Criar branch de validação CI (ex.: `test/instalador-ci-remote`).
2. Abrir PR para branch principal do repositório.
3. Confirmar execução e sucesso de `Instalador CI`.
4. Registrar evidência (URL da run, status dos jobs, artefatos de log).
5. Criar tag de teste controlada (quando aplicável) e validar `Instalador Release`.
6. Registrar evidência da run de release.

## Critério de aceite remoto

- `instalador-ci.yml` executado com sucesso em PR real.
- `instalador-release.yml` executado com sucesso em tag/dispatch real.
- Logs e links anexados em evidência oficial do projeto.

## Regra de score

- Sem evidência remota, `FINAL-15` permanece `PARCIAL`.
- Só promover score de operação após prova de execução real no GitHub Actions.
