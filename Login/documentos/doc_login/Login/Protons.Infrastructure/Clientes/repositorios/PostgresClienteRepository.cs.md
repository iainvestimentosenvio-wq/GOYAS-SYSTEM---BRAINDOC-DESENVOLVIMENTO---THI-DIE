# PostgresClienteRepository.cs

## O que este arquivo faz
Persistencia PostgreSQL de clientes e grupos empresariais, com protecao de dados sensiveis.

## Atualizacoes relevantes
- `Create`/`Update` classificam conflito de unicidade por `ConstraintName`:
  - codigo -> `CodigoClienteDuplicadoException`
  - documento hash -> `DocumentoClienteDuplicadoException`
- Mantem fallback para unicidade sem constraint reconhecida.

## Pontos de atencao
- Busca por documento segue exata via hash.
- Decriptacao em leitura depende de politica BYOK ativa.
