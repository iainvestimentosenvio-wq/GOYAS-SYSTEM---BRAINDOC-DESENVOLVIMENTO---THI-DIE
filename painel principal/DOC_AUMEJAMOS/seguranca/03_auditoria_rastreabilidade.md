# Auditoria e Rastreabilidade

## Eventos auditaveis minimos
- Cadastro criado.
- Cadastro editado.
- Validacao cadastral iniciada/finalizada.
- Decisao de vinculacao de tarefa ao cliente.
- Bloqueio por ambiguidade.

## Correlacao obrigatoria
Cada evento relevante deve incluir:
- usuario responsavel;
- cliente alvo;
- fonte de dados usada;
- decisao aplicada;
- correlation id.

## Padrao minimo de trilha
- Timestamp UTC.
- Acao.
- Resultado.
- Contexto resumido sem expor dado sensivel completo.

## Retencao
Definir periodo de retencao de auditoria compatível com compliance e necessidade operacional.
