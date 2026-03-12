# Documentacao do Instalador

Objetivo: manter somente documentacao ativa e operacional do instalador.

## Regra geral
- Documento ativo fica em `INSTALADOR/documentos/`.
- Evidencia historica nao operacional nao e referencia canonica de continuidade.
- Em conflito entre texto e execucao real, prevalece o comportamento validado.

## Indice canonico
- `comum.md`
- `update.md`
- `testes.md`
- `linux-appimage.md`
- `linux-deb.md`
- `windows-wix.md`
- `windows-inno.md`
- `windows-scripts.md`
- `MATRIZ_EVIDENCIAS_FINAL.md`
- `RELATORIO_COMPATIBILIDADE_LEGADO.md`
- `arquitetura/README.md`
- `referencias/DECISAO_STACK_UPDATE.md`

## Uso recomendado
1. Comecar em `comum.md`.
2. Seguir para o alvo de empacotamento (`linux-*` ou `windows-*`).
3. Fechar com `testes.md` e registrar evidencias vigentes.
