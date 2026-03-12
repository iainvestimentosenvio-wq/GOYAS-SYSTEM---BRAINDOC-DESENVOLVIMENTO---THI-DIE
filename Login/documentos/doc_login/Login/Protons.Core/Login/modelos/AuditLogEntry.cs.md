# AuditLogEntry.cs

## Objetivo
Modelo de dados Audit Log Entry.

## Responsabilidades
- Representar dados do dominio/UI.
- Transportar informacoes entre camadas.

## Dependencias
- Namespace: Protons.Core.Login.Models
- Tipo: class AuditLogEntry

## Principais propriedades
- AuditLogEntry
- Id
- TimestampUtc
- UserId
- EmailSnapshot
- Acao
- Resultado
- Detalhes
- Maquina
- VersaoApp
- PrevHash
- Hash

## Fluxo principal
- Executar as responsabilidades principais conforme a camada do modulo.

## Pontos de atencao
- Validar entradas e estados esperados.
- Manter compatibilidade com as regras de negocio atuais.

## Como testar
- Validar via testes de dominio e fluxo.
