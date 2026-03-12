# PostgresDb.cs

## Objetivo
Gerenciar criação/migração do schema PostgreSQL e validar segurança da camada de dados de clientes.

## Responsabilidades
- Definir conexao e criacao de tabelas.
- Fornecer acesso a DB local/servidor.
- Executar migrações de schema até v5.
- Migrar dados legados para campos cifrados/hash.
- Validar compatibilidade da chave BYOK com dados cifrados existentes.

## Dependencias
- Namespace: Protons.Infrastructure.Login.Database
- Tipo: class PostgresDb

## Principais metodos
- EnsureCreated()
- Open()
- MigrarParaVersao5()
- ValidarCompatibilidadeProtecaoDados()

## Principais propriedades
- PostgresDb
- ConnectionString

## Fluxo principal
1. Abre conexão Postgres.
2. Cria schema base ou aplica migrações pendentes.
3. Garante tabelas/índices de clientes, grupos e tarefas.
4. Valida chave BYOK contra dado cifrado de clientes.
5. Finaliza versão de schema.

## Pontos de atencao
- Falha cedo quando a chave não corresponde ao banco já cifrado.
- Evita operação parcial com leitura inválida de PII.

## Como testar
- Validar via testes de integracao do repositorio.
- Validar abertura com chave correta e falha com chave incorreta.
