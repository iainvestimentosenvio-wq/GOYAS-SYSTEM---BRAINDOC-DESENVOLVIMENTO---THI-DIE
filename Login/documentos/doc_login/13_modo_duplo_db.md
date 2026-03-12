# Modo duplo de banco (Local + Servidor)

## Objetivo
Permitir que o Login funcione em dois modos:
- **Local**: SQLite em cada maquina.
- **Servidor**: banco central (Postgres) compartilhado por varios PCs.

## Como funciona
A aplicacao le `appsettings.json` e decide qual modo usar.
- Se `Mode = "Local"`, usa SQLite local.
- Se `Mode = "Server"`, usa Postgres com a string de conexao informada.

## Arquivo de configuracao
Local esperado (prioridade):
1) `%APPDATA%/Protons/appsettings.json`
2) `appsettings.json` ao lado do executavel

Exemplo:
```
{
  "Database": {
    "Mode": "Local",
    "Sqlite": {
      "Path": ""
    },
    "Server": {
      "Provider": "Postgres",
      "ConnectionString": "Host=localhost;Port=5432;Database=protons;Username=protons;Password=senha"
    }
  },
  "Audit": {
    "UseHashChain": false
  }
}
```

### Campo `Path` (Local)
- Vazio: usa `%APPDATA%/Protons/protons.db`.
- Preenchido: usa o caminho informado.

### Campo `ConnectionString` (Servidor)
- Obrigatorio em modo servidor.
- Se estiver vazio, a aplicacao falha com erro explicito.

## Observacoes
- **Multi-PC**: para varios computadores ao mesmo tempo, use **Servidor**. SQLite em pasta compartilhada nao e recomendado.
- **Auditoria com hash chain**: em modo servidor, a cadeia e segura mesmo com varias instancias.

## SQL inicial (Postgres)
As tabelas sao criadas automaticamente no primeiro uso. Se quiser criar manualmente, consulte `Protons.Infrastructure/Login/banco_de_dados/PostgresDb.cs`.
