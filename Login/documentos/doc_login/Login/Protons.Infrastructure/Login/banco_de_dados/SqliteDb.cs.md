# SqliteDb.cs

## Objetivo
Gerenciar criação/migração do schema SQLite e validar segurança da camada de dados de clientes.

## Responsabilidades
- Definir conexao e criacao de tabelas.
- Fornecer acesso a DB local/servidor.
- Executar migrações de schema (até v5).
- Criar backup automático antes de migrações críticas.
- Validar compatibilidade da chave de proteção com dados já cifrados.

## Dependencias
- Namespace: Protons.Infrastructure.Login.Database
- Tipo: class SqliteDb

## Principais metodos
- EnsureCreated()
- Open()
- MigrarParaVersao5()
- ValidarCompatibilidadeProtecaoDados()

## Principais propriedades
- SqliteDb
- DbPath

## Fluxo principal
1. Abre conexão.
2. Cria schema base ou aplica migrações pendentes.
3. Garante schema de clientes/grupos/tarefas.
4. Valida chave BYOK contra `DocumentoCipher` existente (fail fast).
5. Define versão final do schema.

## Pontos de atencao
- Se existir dado cifrado `v1:` e a chave BYOK estiver ausente/incorreta, o startup é bloqueado com erro explícito.
- Evita cenário de "dados sumiram" causado por decriptação inválida tardia.

## Como testar
- Validar via testes de integracao do repositorio.
- Validar migração v4->v5 com backup.
- Validar falha ao abrir base cifrada com chave incorreta.
