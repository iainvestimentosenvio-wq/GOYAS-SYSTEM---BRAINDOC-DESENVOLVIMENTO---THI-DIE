# INDEX — Projeto PROTONS

Ponto de entrada único da documentação. Fonte de verdade para navegação.

---

## Início rápido

1. Leia `GUIA_CONTINUIDADE_IDE.md` — retomar o projeto, build, painel, BYOK.
2. Build e testes antes de qualquer alteração:
   ```bash
   cd "Login" && dotnet build Protons.sln -c Debug
   cd "Login" && dotnet test Protons.sln -c Debug
   ```
3. Painel em desenvolvimento:
   ```bash
   cd "Login" && bash scripts/hot_reload_painel_direto.sh
   ```

---

## Documentos canônicos

### Raiz
| Doc | Conteúdo |
|-----|----------|
| `GUIA_CONTINUIDADE_IDE.md` | Retomar projeto, bypass dev, persistência, BYOK |
| `AGENTS.md` | Instruções para Cursor/agentes |
| `docs/auto-sync-colaboracao.md` | Auto-sync Git (estabilizacao-fase1) |

### Login (`Login/`)
| Doc | Conteúdo |
|-----|----------|
| `Login/README.md` | Stack, arquitetura, build, testes |
| `Login/documentos/DOCS_OVERVIEW.md` | Padrão de documentação |
| `Login/documentos/doc_login/` | Docs temáticos (visão, requisitos, arquitetura, testes) |

### Painel principal (`painel principal/`)
| Doc | Conteúdo |
|-----|----------|
| `painel principal/LEIA-ME.md` | Visão do módulo |
| `painel principal/documentacao/` | Fluxos (drag-drop, aprovação, cadastro, build) |
| `painel principal/codigos/.../ancorar_pdf/documentacao/` | ADR scheduler, índice |

### Instalador (`INSTALADOR/`)
| Doc | Conteúdo |
|-----|----------|
| `INSTALADOR/LEIA-ME-PRIMEIRO.txt` | Comandos e variáveis |
| `INSTALADOR/CHECKLIST.md` | Guia operacional (Windows, Linux) |
| `INSTALADOR/documentos/` | comum, testes, update, linux-*, windows-* |

---

## Regras de documentação

1. Código é fonte de verdade; docs complementam.
2. Docs espelho apenas para módulos críticos (ver `DOCS_OVERVIEW.md`).
3. Um assunto = um documento. Sem duplicatas.
4. Mudou código → atualizar doc correspondente no mesmo commit.
