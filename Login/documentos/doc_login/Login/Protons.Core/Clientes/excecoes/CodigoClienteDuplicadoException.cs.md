# CodigoClienteDuplicadoException.cs

## O que este arquivo faz
Representa violacao de unicidade de `CodigoCliente` na persistencia.

## Objetivo
Permitir que o servico diferencie duplicidade de codigo versus duplicidade de documento.

## Pontos de atencao
- Deve ser lancada apenas para conflito de codigo.
- Nao usar para outros erros de unicidade.
