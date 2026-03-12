# SqliteClienteRepository.cs

## O que este arquivo faz
Persistencia SQLite de clientes e grupos empresariais, com protecao de dados sensiveis.

## Atualizacoes relevantes
- `Create`/`Update` classificam conflito de unicidade:
  - `CodigoCliente` -> `CodigoClienteDuplicadoException`
  - `DocumentoHash` -> `DocumentoClienteDuplicadoException`
- Mantem fallback para erro de unicidade nao classificado.

## Pontos de atencao
- Busca por documento permanece exata por hash.
- Campos sensiveis continuam cifrados em repouso.
