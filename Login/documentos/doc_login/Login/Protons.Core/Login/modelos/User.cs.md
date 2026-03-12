# User.cs

## Objetivo
Modelo de dados User.

## Responsabilidades
- Representar dados do dominio/UI.
- Transportar informacoes entre camadas.

## Dependencias
- Namespace: Protons.Core.Login.Models
- Tipo: class User

## Principais propriedades
- User
- Id
- Empresa
- Nome
- Cpf
- Cargo
- Email
- SenhaHash
- SenhaSalt
- IteracoesPbkdf2
- Status
- Role

## Fluxo principal
- Executar as responsabilidades principais conforme a camada do modulo.

## Pontos de atencao
- Validar entradas e estados esperados.
- Manter compatibilidade com as regras de negocio atuais.

## Como testar
- Validar via testes de dominio e fluxo.
