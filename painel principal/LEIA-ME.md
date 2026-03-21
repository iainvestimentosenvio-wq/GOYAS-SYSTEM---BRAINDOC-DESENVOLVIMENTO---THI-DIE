# Painel Principal - Guia Rapido

## Estrutura atual

- `codigos/painel_principal/funcionalidades/painel/`
  - `interface/`
  - `modelos_de_visao/`
- `codigos/painel_principal/funcionalidades/ferramentas/`
  - `ancorar_pdf/`

## Como rodar o painel direto (bypass de login)

O painel pode abrir **sem tela de login** para desenvolvimento. Exige **todas** as condições:

| Condição | Como ativar |
|----------|-------------|
| Build Debug | `dotnet build -c Debug` (padrão do script) |
| Ambiente Development | `ASPNETCORE_ENVIRONMENT=Development` (o script define) |
| Policy permitida | `appsettings.json` → `Security.Painel.PermitirBypassPainelDireto: true` **ou** env `PROTONS_PAINEL_DIRETO_HABILITADO=1` |
| Sessão solicita bypass | env `PROTONS_PAINEL_DIRETO=1` (o script define) |

**Comando (a partir desta pasta):**
```bash
cd "../Login" && bash scripts/hot_reload_painel_direto.sh
```

Sem hot reload (mais estável):
```bash
cd "../Login" && bash scripts/hot_reload_painel_direto.sh no-hot-reload
```

**Banco local do bypass:** `~/.local/share/protons-dev/Protons/protons.db` (Linux)

## Como validar o projeto (build e testes)

A partir desta pasta (`painel principal`):
```bash
cd "../Login" && dotnet build Protons.sln -c Debug
cd "../Login" && dotnet test Protons.sln -c Debug
```

## Regra de organizacao

- Tudo que for tela/comportamento do painel fica em `funcionalidades/painel`.
- Cada ferramenta nova ganha uma pasta propria em `funcionalidades/ferramentas/<nome_da_ferramenta>`.

## Documentacao tecnica

- Entrada geral: `documentacao/README.md`
- Indice do painel: `documentacao/painel/INDICE.md`

## Documentacao de futuro

- Planejamento de evolucao: issues e backlog do projeto
