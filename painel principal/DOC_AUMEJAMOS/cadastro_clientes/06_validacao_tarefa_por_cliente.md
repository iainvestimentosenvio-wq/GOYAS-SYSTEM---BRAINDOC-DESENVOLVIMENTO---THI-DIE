# Validacao de Tarefa por Cliente

## Objetivo
Garantir que tarefa/documento seja associado ao cliente correto antes da execucao automatica.

## Algoritmo de vinculacao (alto nivel)
1. Extrair e normalizar documento do artefato (nota/arquivo).
2. Buscar cliente por documento e, quando aplicavel, por grupo.
3. Avaliar cenarios de match.

## Cenarios
- Match unico: vincular cliente e seguir fluxo.
- Sem match: bloquear automacao e abrir pendencia.
- Multiplos matches: bloquear automacao por ambiguidade e exigir revisao humana.

## Politica antifalso-positivo
Em qualquer ambiguidade, prevalece seguranca operacional:
- nao executar automaticamente;
- registrar evidencia;
- enviar para fila de conferencia.

## Evidencias e logs
Registrar no evento:
- documento analisado (mascarado quando exibido);
- cliente(s) candidato(s);
- regra aplicada;
- decisao final.
