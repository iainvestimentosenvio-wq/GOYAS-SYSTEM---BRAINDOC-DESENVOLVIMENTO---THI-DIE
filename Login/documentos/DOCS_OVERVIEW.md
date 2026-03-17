# Documentação — Módulo Login

## Objetivo
Documentar decisões, fluxos e módulos principais. O código é a fonte de verdade; docs complementam.

## Estrutura
```
documentos/
├── DOCS_OVERVIEW.md     # Este arquivo
├── doc_login/
│   ├── 00_visao_geral.md
│   ├── 01_requisitos.md
│   ├── 05_arquitetura_do_codigo.md
│   └── ... (docs temáticos por assunto)
└── doc_login/Login/     # Docs espelho (opcional para módulos críticos)
```

## Regra de documentação
- **Módulos críticos:** criar `.md` espelho quando o código for complexo ou decisões precisarem ser explicadas.
- **Mudou código:** atualizar doc correspondente no mesmo commit.
- **Um assunto = um documento.** Sem duplicatas.

## Referências
- `../../INDEX.md` — mapa da documentação
- `../../GUIA_CONTINUIDADE_IDE.md` — retomar o projeto
