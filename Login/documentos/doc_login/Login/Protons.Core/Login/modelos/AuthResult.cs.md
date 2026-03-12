# AuthResult.cs

## Objetivo
Modelo de dados Auth Result.

## Responsabilidades
- Representar dados do dominio/UI.
- Transportar informacoes entre camadas.

## Dependencias
- Namespace: Protons.Core.Login.Models
- Tipo: class AuthResult

## Principais propriedades
- AuthResult
- Sucesso
- Mensagem
- Bloqueado
- Pendente
- SemPermissao
- UserId
- Nome
- Role

## Fluxo principal
- Executar as responsabilidades principais conforme a camada do modulo.

## Pontos de atencao
- Validar entradas e estados esperados.
- Manter compatibilidade com as regras de negocio atuais.

## Como testar
- Validar via testes de dominio e fluxo.
