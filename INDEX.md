# INDEX — Projeto PROTONS

Ponto de entrada único para navegar na documentação vigente.
Contém apenas referências a arquivos que existem no repositório.

---

## Início rápido

1. Leia `GUIA_CONTINUIDADE_IDE.md` — contexto completo para retomar o projeto.
2. Faça build e rode os testes antes de qualquer alteração.
3. Ao criar código novo, crie o doc espelho correspondente no mesmo commit.

---

## Módulos e seus documentos canônicos

### Raiz do repositório

| Documento | Descrição |
|---|---|
| `GUIA_CONTINUIDADE_IDE.md` | Como retomar o projeto, subir o painel, bypass dev, BYOK, diagnóstico |
| `INDEX.md` | Este arquivo — mapa da documentação |

### Login (`Login/`)

| Documento | Descrição |
|---|---|
| `Login/README.md` | Stack, estrutura, comandos de build e teste |
| `Login/documentos/DOCS_OVERVIEW.md` | Padrão de documentação espelho 1:1 |
| `Login/documentos/doc_login/GUIA_DE_TRABALHO.md` | Regras de trabalho e sincronização de docs |
| `Login/documentos/doc_login/RESULTADOS_EVOLUCAO_TESTES.md` | Histórico de evolução da suite de testes |

Os docs espelho individuais (um `.md` por `.cs`/`.axaml`) ficam em
`Login/documentos/doc_login/Login/` — consulte `DOCS_OVERVIEW.md` para o padrão.

### Painel principal (`painel principal/`)

| Documento | Descrição |
|---|---|
| `painel principal/documentacao/README.md` | Princípios de documentação do módulo |
| `painel principal/documentacao/painel/INDICE.md` | Índice dos docs do painel |
| `painel principal/documentacao/painel/drag_drop/PAINEL_DRAG_DROP.md` | Fluxo de drag-and-drop de ferramentas |
| `painel principal/documentacao/painel/operacao/APROVACAO_USUARIOS.md` | Fluxo de aprovação de novos usuários |
| `painel principal/documentacao/painel/operacao/PERSISTENCIA_E_CADASTRO_CLIENTES.md` | Persistência e cadastro de clientes |
| `painel principal/documentacao/painel/operacao/SELETOR_CLIENTE_UNIFICADO.md` | Seletor unificado de cliente |
| `painel principal/documentacao/painel/operacao/BUILD_REPRODUCIVEL_ACL.md` | Build reproduzível em ambiente Linux compartilhado |
| `painel principal/DOC_AUMEJAMOS/README.md` | Planejamento futuro (estado atual → estado alvo) |

Ferramenta `ancorar_pdf`:
- `painel principal/codigos/.../ancorar_pdf/documentacao/ADR-001_scheduler.md` — decisão de scheduler (Quartz vs Cronos)
- `painel principal/codigos/.../ancorar_pdf/documentacao/INDICE.md` — índice da ferramenta

### Instalador (`INSTALADOR/`)

| Documento | Descrição |
|---|---|
| `INSTALADOR/documentos/README.md` | Visão geral do instalador |
| `INSTALADOR/documentos/comum.md` | Guia de ativos e recursos compartilhados |
| `INSTALADOR/documentos/testes.md` | Estratégia de testes do instalador |
| `INSTALADOR/documentos/update.md` | Ciclo de atualização |

---

## Comandos de referência rápida

```bash
# Build
cd "Login" && dotnet build Protons.sln -c Debug

# Testes
cd "Login" && dotnet test Protons.sln -c Debug

# Painel direto (desenvolvimento)
cd "Login" && bash scripts/hot_reload_painel_direto.sh
```

---

## Regras de documentação (obrigatórias)

1. Cada arquivo `.cs` e `.axaml` de produção deve ter um `.md` espelho com o mesmo caminho relativo.
2. Não criar checklists de execução, rascunhos ou docs de pendência no branch ativo.
3. Não manter docs duplicadas para o mesmo assunto — um assunto, um documento.
4. Mudou o código → mudou o doc espelho no mesmo commit.
