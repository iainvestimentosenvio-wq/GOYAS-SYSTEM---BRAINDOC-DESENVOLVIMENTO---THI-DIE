# PostgresUserRepository.cs

## Objetivo
Repositorio Postgres User Repository para acesso a dados.

## Responsabilidades
- Persistencia e leitura de dados.
- Mapeamento entre dominio e storage.

## Dependencias
- Namespace: Protons.Infrastructure.Login.Repositories
- Tipo: class PostgresUserRepository

## Principais metodos
- GetByEmail()
- GetById()
- GetPendentes()
- HasAnyUsers()
- Create()
- Update()

## Fluxo principal
- Executar as responsabilidades principais conforme a camada do modulo.

## Pontos de atencao
- Validar entradas e estados esperados.
- Manter compatibilidade com as regras de negocio atuais.

## Como testar
- Coberto por testes de repositorio/infra.
