# DOCS_OVERVIEW - Documentacao espelhada

## Objetivo
Explicar o padrao de documentacao 1:1 com o codigo e orientar navegacao.

## Regra 1:1 (espelho)
Para cada arquivo de codigo de producao (`.cs`/`.axaml`), existe um `.md` com o mesmo caminho em `Login/documentos/doc_login/`.

Exemplos:
- `Login/Protons.Core/Login/servicos/AuthService.cs`
  -> `Login/documentos/doc_login/Login/Protons.Core/Login/servicos/AuthService.cs.md`
- `Login/Protons.UI/Login/telas/LoginView.axaml`
  -> `Login/documentos/doc_login/Login/Protons.UI/Login/telas/LoginView.axaml.md`

## Onde comecar
- `../INDEX.md`
- `../GUIA_CONTINUIDADE_IDE.md`
- `Login/documentos/README.md`
- `Login/documentos/doc_login/GUIA_DE_TRABALHO.md`

## Estrutura macro (resumo)
```text
documentos/
├── README.md
├── DOCS_OVERVIEW.md
├── doc_login/
│   ├── 00_visao_geral.md
│   ├── 01_requisitos.md
│   ├── 05_arquitetura_do_codigo.md
│   ├── 07_plano_de_testes.md
│   ├── 11_documento_testes.md
│   ├── 14_roteiro_testes_manuais.md
│   ├── RESULTADOS_EVOLUCAO_TESTES.md
│   ├── doc_testes/
│   └── Login/Protons.* (espelho do codigo)
└── TEMPLATES/
```

## Regra de atualizacao
- Mudou codigo -> atualizar doc espelho correspondente.
- Codigo novo -> criar doc espelho no mesmo caminho relativo.
