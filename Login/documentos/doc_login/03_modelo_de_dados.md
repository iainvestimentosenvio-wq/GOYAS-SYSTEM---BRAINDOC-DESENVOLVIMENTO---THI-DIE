# 03 - Modelo de Dados (SQLite)

## Visão geral
Persistência local em arquivo `.db` dentro do diretório de dados do usuário:
- **Windows**: `%AppData%\Protons`
- **Linux**: `XDG_DATA_HOME/Protons` (fallback `~/.local/share/Protons`)

O modelo foi desenhado para **auditoria** e **segurança** no MVP offline.

## Tabelas principais
### 1) `Users`
Armazena credenciais e status do usuário.

**Campos sugeridos**:
- `Id` (INTEGER, PK, AUTOINCREMENT)
- `Empresa` (TEXT, NOT NULL)
- `Nome` (TEXT, NOT NULL)
- `CPF` (TEXT, NOT NULL)
- `Cargo` (TEXT, NOT NULL)
- `Email` (TEXT, NOT NULL, UNIQUE)
- `SenhaHash` (BLOB/TEXT, NOT NULL)
- `SenhaSalt` (BLOB/TEXT, NOT NULL)
- `IteracoesPBKDF2` (INTEGER, NOT NULL)
- `Status` (TEXT, NOT NULL) — **PENDENTE / ATIVO / BLOQUEADO**
- `Role` (TEXT, NOT NULL) — **Admin / Usuario** (perfil)
- `FalhasLogin` (INTEGER, NOT NULL, DEFAULT 0)
- `LockoutAteUtc` (TEXT, NULL)
- `CriadoEmUtc` (TEXT, NOT NULL)
- `AtualizadoEmUtc` (TEXT, NOT NULL)
- `UltimoLoginUtc` (TEXT, NULL)

**Índices sugeridos**:
- `UX_Users_Email` (UNIQUE em `Email`)
- `IX_Users_Status` (em `Status`)

### 2) `AuditLog`
Registra ações críticas do sistema.

**Campos mínimos**:
- `Id` (INTEGER, PK, AUTOINCREMENT)
- `TimestampUtc` (TEXT, NOT NULL)
- `UserId` (INTEGER, NULL)
- `EmailSnapshot` (TEXT, NULL)
- `Acao` (TEXT, NOT NULL) — ex.: LOGIN_SUCESSO, LOGIN_FALHA, CRIAR_CONTA
- `Resultado` (TEXT, NOT NULL) — OK / ERRO
- `Detalhes` (TEXT, NULL) — **sem dados sensíveis**
- `Maquina` (TEXT, NOT NULL)
- `VersaoApp` (TEXT, NOT NULL)
- `PrevHash` (TEXT, NULL) — opcional (cadeia de hash)
- `Hash` (TEXT, NULL) — opcional (cadeia de hash)

**Índices sugeridos**:
- `IX_AuditLog_Timestamp` (em `TimestampUtc`)
- `IX_AuditLog_UserId` (em `UserId`)

## Exemplo de esquema (SQLite)
```sql
CREATE TABLE Users (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Empresa TEXT NOT NULL,
  Nome TEXT NOT NULL,
  CPF TEXT NOT NULL,
  Cargo TEXT NOT NULL,
  Email TEXT NOT NULL UNIQUE,
  SenhaHash TEXT NOT NULL,
  SenhaSalt TEXT NOT NULL,
  IteracoesPBKDF2 INTEGER NOT NULL,
  Status TEXT NOT NULL,
  Role TEXT NOT NULL,
  FalhasLogin INTEGER NOT NULL DEFAULT 0,
  LockoutAteUtc TEXT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  UltimoLoginUtc TEXT NULL
);

CREATE TABLE AuditLog (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TimestampUtc TEXT NOT NULL,
  UserId INTEGER NULL,
  EmailSnapshot TEXT NULL,
  Acao TEXT NOT NULL,
  Resultado TEXT NOT NULL,
  Detalhes TEXT NULL,
  Maquina TEXT NOT NULL,
  VersaoApp TEXT NOT NULL,
  PrevHash TEXT NULL,
  Hash TEXT NULL,
  FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX IX_Users_Status ON Users(Status);
CREATE INDEX IX_AuditLog_Timestamp ON AuditLog(TimestampUtc);
CREATE INDEX IX_AuditLog_UserId ON AuditLog(UserId);
```

## Observações
- Datas em UTC no formato ISO 8601 (`yyyy-MM-ddTHH:mm:ss.fffZ`).
- CPF armazenado como texto (com ou sem máscara), validado na camada de domínio.
- `EmailSnapshot` evita perda de rastreio caso o usuário seja removido no futuro.
