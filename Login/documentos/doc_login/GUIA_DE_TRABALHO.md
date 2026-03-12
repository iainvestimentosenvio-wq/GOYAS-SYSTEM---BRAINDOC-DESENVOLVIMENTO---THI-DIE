# Guia de Trabalho - Documentacao e Organizacao

## Objetivo
Manter codigo e documentacao sincronizados com espelhamento 1:1.

## Regras obrigatorias
1. Mudou codigo, mudou doc espelho.
2. Arquivo novo exige doc espelho.
3. Estrutura de `doc_login` deve espelhar o codigo.
4. Resultados de testes relevantes devem ser registrados em `RESULTADOS_EVOLUCAO_TESTES.md`.
5. Nao manter checklist temporaria ou doc de pendencias aberta no branch ativo.

## Padrao minimo de cada doc espelho
- O que este arquivo faz
- Entradas e saidas
- Pontos de atencao
- Quando alterar

## Onde atualizar
- Docs espelho: `Login/documentos/doc_login/Login/...`
- Resultados de testes: `Login/documentos/doc_login/RESULTADOS_EVOLUCAO_TESTES.md`
- Mapa de testes: `Login/documentos/doc_login/doc_testes/README.md`
