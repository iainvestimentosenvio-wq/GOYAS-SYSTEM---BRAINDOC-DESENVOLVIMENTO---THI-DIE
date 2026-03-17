using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Protons.Core.Clientes.Security;
using Protons.Core.Clientes.Validation;

namespace Protons.Infrastructure.Login.Database;

public sealed class SqliteDb
{
    private const string SchemaVersionKey = "SchemaVersion";
    private const int SchemaVersionAtual = 17;

    private readonly IClienteDataProtector? _dataProtector;
    private readonly bool _allowLegacyPlaintext;

    public string DbPath { get; }
    public string ConnectionString => $"Data Source={DbPath}";

    public SqliteDb(string dbPath, IClienteDataProtector? dataProtector = null, bool allowLegacyPlaintext = true)
    {
        DbPath = Path.IsPathRooted(dbPath) ? dbPath : Path.GetFullPath(dbPath);
        _dataProtector = dataProtector;
        _allowLegacyPlaintext = allowLegacyPlaintext;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath) ?? ".");

        string? backupPath = null;
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            if (!SchemaExists(connection))
            {
                CriarSchemaBase(connection);
                EnsureGruposSchema(connection);
                EnsureClientesSchema(connection);
                EnsureTarefasSchema(connection);
                EnsureExecucaoSchema(connection);
                MigrarParaVersao10(connection);
                MigrarParaVersao11(connection);
                MigrarParaVersao12(connection);
                MigrarParaVersao13(connection);
                MigrarParaVersao14(connection);
                MigrarParaVersao15(connection);
                MigrarParaVersao16(connection);
                MigrarParaVersao17(connection);
                EnsureSchemaMetadataTable(connection);
                ValidarCompatibilidadeProtecaoDados(connection);
                DefinirVersaoSchema(connection, SchemaVersionAtual);
                return;
            }

            EnsureSchemaMetadataTable(connection);
            var versao = ObterVersaoSchema(connection);

            if (versao < 5)
                backupPath = CriarBackup(connection);

            if (versao < 2)
            {
                MigrarParaVersao2(connection);
            }
            if (versao < 3)
            {
                MigrarParaVersao3(connection);
            }
            if (versao < 4)
            {
                MigrarParaVersao4(connection);
            }
            if (versao < 5)
            {
                MigrarParaVersao5(connection);
            }
            if (versao < 6)
            {
                MigrarParaVersao6(connection);
            }
            if (versao < 7)
            {
                MigrarParaVersao7(connection);
            }
            if (versao < 8)
            {
                MigrarParaVersao8(connection);
            }
            if (versao < 9)
            {
                MigrarParaVersao9(connection);
            }
            if (versao < 10)
            {
                MigrarParaVersao10(connection);
            }
            if (versao < 11)
            {
                MigrarParaVersao11(connection);
            }
            if (versao < 12)
            {
                MigrarParaVersao12(connection);
            }
            if (versao < 13)
            {
                MigrarParaVersao13(connection);
            }
            if (versao < 14)
            {
                MigrarParaVersao14(connection);
            }
            if (versao < 15)
            {
                MigrarParaVersao15(connection);
            }
            if (versao < 16)
            {
                MigrarParaVersao16(connection);
            }
            if (versao < 17)
            {
                MigrarParaVersao17(connection);
            }

            EnsureGruposSchema(connection);
            EnsureClientesSchema(connection);
            EnsureTarefasSchema(connection);
            ValidarCompatibilidadeProtecaoDados(connection);
            DefinirVersaoSchema(connection, SchemaVersionAtual);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(backupPath))
        {
            TentarRestaurarBackup(backupPath!);
            throw new InvalidOperationException($"Falha ao migrar schema SQLite para versão 10. Backup restaurado. Motivo: {ex.Message}", ex);
        }
    }

    private static void CriarSchemaBase(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
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
  LockoutsConsecutivos INTEGER NOT NULL DEFAULT 0,
  LockoutAteUtc TEXT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  UltimoLoginUtc TEXT NULL
);

CREATE TABLE IF NOT EXISTS AuditLog (
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

CREATE TABLE IF NOT EXISTS AuditChain (
  Id INTEGER PRIMARY KEY CHECK (Id = 1),
  LastHash TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_Users_Status ON Users(Status);
CREATE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email);
CREATE INDEX IF NOT EXISTS IX_Users_LockoutAteUtc ON Users(LockoutAteUtc);
CREATE INDEX IF NOT EXISTS IX_AuditLog_Timestamp ON AuditLog(TimestampUtc);
CREATE INDEX IF NOT EXISTS IX_AuditLog_UserId ON AuditLog(UserId);

INSERT OR IGNORE INTO AuditChain (Id, LastHash) VALUES (1, NULL);
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureClientesSchema(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Clientes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CodigoCliente TEXT NOT NULL,
  Nome TEXT NOT NULL,
  NomeFantasia TEXT NULL,
  TipoDocumento TEXT NOT NULL,
  DocumentoHash TEXT NOT NULL,
  DocumentoCipher TEXT NOT NULL,
  GrupoEmpresarialId INTEGER NULL,
  EmailCipher TEXT NULL,
  TelefoneCipher TEXT NULL,
  ProtecaoVersao INTEGER NOT NULL DEFAULT 1,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (GrupoEmpresarialId) REFERENCES GruposEmpresariais(Id)
);

DROP INDEX IF EXISTS IX_Clientes_Documento;
DROP INDEX IF EXISTS UX_Clientes_Documento_Ativo;

CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_CodigoCliente ON Clientes(CodigoCliente);
CREATE INDEX IF NOT EXISTS IX_Clientes_Nome ON Clientes(Nome);
CREATE INDEX IF NOT EXISTS IX_Clientes_NomeFantasia ON Clientes(NomeFantasia);
CREATE INDEX IF NOT EXISTS IX_Clientes_Ativo ON Clientes(Ativo);
CREATE INDEX IF NOT EXISTS IX_Clientes_GrupoEmpresarialId ON Clientes(GrupoEmpresarialId);
CREATE INDEX IF NOT EXISTS IX_Clientes_DocumentoHash ON Clientes(DocumentoHash);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_DocumentoHash_Ativo ON Clientes(DocumentoHash) WHERE Ativo = 1;
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureClientesSchemaVersao4(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Clientes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CodigoCliente TEXT NOT NULL,
  Nome TEXT NOT NULL,
  NomeFantasia TEXT NULL,
  TipoDocumento TEXT NOT NULL,
  Documento TEXT NOT NULL,
  GrupoEmpresarialId INTEGER NULL,
  Email TEXT NULL,
  Telefone TEXT NULL,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (GrupoEmpresarialId) REFERENCES GruposEmpresariais(Id)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_CodigoCliente ON Clientes(CodigoCliente);
CREATE INDEX IF NOT EXISTS IX_Clientes_Nome ON Clientes(Nome);
CREATE INDEX IF NOT EXISTS IX_Clientes_NomeFantasia ON Clientes(NomeFantasia);
CREATE INDEX IF NOT EXISTS IX_Clientes_Ativo ON Clientes(Ativo);
CREATE INDEX IF NOT EXISTS IX_Clientes_Documento ON Clientes(Documento);
CREATE INDEX IF NOT EXISTS IX_Clientes_GrupoEmpresarialId ON Clientes(GrupoEmpresarialId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_Documento_Ativo ON Clientes(Documento) WHERE Ativo = 1;
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureGruposSchema(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS GruposEmpresariais (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Nome TEXT NOT NULL,
  NomeNormalizado TEXT NOT NULL,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_GruposEmpresariais_Nome ON GruposEmpresariais(Nome);
CREATE UNIQUE INDEX IF NOT EXISTS UX_GruposEmpresariais_NomeNormalizado ON GruposEmpresariais(NomeNormalizado);
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureTarefasSchema(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Tarefas (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ClienteId INTEGER NOT NULL,
  FerramentaId TEXT NOT NULL DEFAULT 'generica',
  Titulo TEXT NOT NULL,
  VencimentoUtc TEXT NOT NULL,
  ResponsavelUserId INTEGER NOT NULL,
  Status TEXT NOT NULL,
  Recorrencia TEXT NOT NULL,
  DiaRecorrenciaMensal INTEGER NULL,
  Ativa INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  ConcluidaEmUtc TEXT NULL,
  FOREIGN KEY (ClienteId) REFERENCES Clientes(Id),
  FOREIGN KEY (ResponsavelUserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS ClientePermissoesUsuarios (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ClienteId INTEGER NOT NULL,
  UserId INTEGER NOT NULL,
  PodeEditar INTEGER NOT NULL DEFAULT 0,
  ConcedidoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (ClienteId) REFERENCES Clientes(Id),
  FOREIGN KEY (UserId) REFERENCES Users(Id),
  FOREIGN KEY (ConcedidoPorUserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS AncorarPdfArquivosProcessadosCiclo (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  CicloId TEXT NOT NULL,
  NomeEsperadoLogico TEXT NOT NULL,
  ArquivoHash TEXT NOT NULL,
  ArquivoPath TEXT NOT NULL,
  TamanhoBytes INTEGER NOT NULL,
  MtimeUtc TEXT NOT NULL,
  ProcessadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE TABLE IF NOT EXISTS AncorarPdfConfiguracoesTarefa (
  TarefaId INTEGER PRIMARY KEY,
  ClienteId INTEGER NOT NULL,
  EsteiraId INTEGER NOT NULL,
  NomeTarefaPersonalizado TEXT NOT NULL,
  PastaMonitoradaPath TEXT NOT NULL,
  PdfModeloPath TEXT NOT NULL,
  NomeReferenciaArquivo TEXT NOT NULL,
  MonitorarSubpastas INTEGER NOT NULL DEFAULT 0,
  ValidacaoClienteAtiva INTEGER NOT NULL DEFAULT 1,
  LimiarSimilaridadeNome REAL NOT NULL DEFAULT 0.75,
  HighlightOpacity REAL NOT NULL DEFAULT 0.40,
  ModoSelecao TEXT NOT NULL DEFAULT 'RetanguloLivre',
  PdfModeloCrossCliente INTEGER NOT NULL DEFAULT 0,
  PdfModeloCrossClienteJustificativa TEXT NULL,
  Recorrencia TEXT NOT NULL,
  AgendamentoSegundo INTEGER NOT NULL DEFAULT 0,
  TimezoneId TEXT NOT NULL DEFAULT 'UTC',
  PrioridadeExecucao INTEGER NOT NULL DEFAULT 3,
  DstHorarioInvalidoPolicy TEXT NOT NULL DEFAULT 'AvancarParaProximoHorarioValido',
  DstHorarioAmbiguoPolicy TEXT NOT NULL DEFAULT 'PreferirOffsetMaisCedo',
  TemplateAncorasJson TEXT NOT NULL,
  OcrFallbackAtivo INTEGER NOT NULL DEFAULT 0,
  OcrDpi INTEGER NOT NULL DEFAULT 300,
  OcrLang TEXT NOT NULL DEFAULT 'por+eng',
  ProgramadoPorUserId INTEGER NOT NULL,
  ProgramadoPorNome TEXT NOT NULL,
  ProgramadoEmUtc TEXT NOT NULL,
  AtualizadoPorUserId INTEGER NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  VersaoTemplate INTEGER NOT NULL DEFAULT 1,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE TABLE IF NOT EXISTS AncorarPdfTemplateHistorico (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  Versao INTEGER NOT NULL,
  AntesJson TEXT NOT NULL,
  DepoisJson TEXT NOT NULL,
  AlteradoPorUserId INTEGER NOT NULL,
  AlteradoPorNome TEXT NOT NULL,
  AlteradoEmUtc TEXT NOT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE TABLE IF NOT EXISTS AncorarPdfSchedulerBacklogPendente (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  JanelaAlvoUtc TEXT NOT NULL,
  AtrasoSegundos INTEGER NOT NULL,
  Motivo TEXT NOT NULL,
  DetectadoEmUtc TEXT NOT NULL,
  Status TEXT NOT NULL DEFAULT 'Pendente',
  DecididoEmUtc TEXT NULL,
  DecididoPorUserId INTEGER NULL,
  DecididoPorNome TEXT NULL,
  Observacao TEXT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE TABLE IF NOT EXISTS AncorarPdfSchedulerEventos (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  TipoEvento TEXT NOT NULL,
  Detalhes TEXT NULL,
  OcorreuEmUtc TEXT NOT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE INDEX IF NOT EXISTS IX_Tarefas_Cliente_Status_Vencimento ON Tarefas(ClienteId, Status, VencimentoUtc);
CREATE INDEX IF NOT EXISTS IX_Tarefas_Cliente_Ferramenta_Status_Vencimento ON Tarefas(ClienteId, FerramentaId, Status, VencimentoUtc);
CREATE INDEX IF NOT EXISTS IX_Tarefas_Responsavel ON Tarefas(ResponsavelUserId);
CREATE INDEX IF NOT EXISTS IX_Tarefas_Cliente_Ativa ON Tarefas(ClienteId, Ativa);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ClientePermissoes_Cliente_User ON ClientePermissoesUsuarios(ClienteId, UserId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AncorarPdf_ArquivoProcessadoCiclo_Unico ON AncorarPdfArquivosProcessadosCiclo(TarefaId, CicloId, NomeEsperadoLogico);
CREATE INDEX IF NOT EXISTS IX_AncorarPdf_ArquivoProcessadoCiclo_Tarefa_ProcessadoEm ON AncorarPdfArquivosProcessadosCiclo(TarefaId, ProcessadoEmUtc);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfConfig_TarefaId ON AncorarPdfConfiguracoesTarefa(TarefaId);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfConfig_Cliente_Esteira ON AncorarPdfConfiguracoesTarefa(ClienteId, EsteiraId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfConfig_Cliente_Esteira_NomeAtivo ON AncorarPdfConfiguracoesTarefa(ClienteId, EsteiraId, NomeTarefaPersonalizado);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfConfig_Prioridade ON AncorarPdfConfiguracoesTarefa(PrioridadeExecucao, TarefaId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfTemplateHistorico_Tarefa_Versao ON AncorarPdfTemplateHistorico(TarefaId, Versao);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSchedulerBacklog_Tarefa_Janela ON AncorarPdfSchedulerBacklogPendente(TarefaId, JanelaAlvoUtc);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSchedulerBacklog_Cliente_Status ON AncorarPdfSchedulerBacklogPendente(ClienteId, Status, DetectadoEmUtc);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSchedulerEventos_Tarefa_Data ON AncorarPdfSchedulerEventos(TarefaId, OcorreuEmUtc);
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureSchemaMetadataTable(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SchemaMetadata (
  Chave TEXT PRIMARY KEY,
  Valor TEXT NOT NULL
);
";
        cmd.ExecuteNonQuery();
    }

    private static int ObterVersaoSchema(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Valor FROM SchemaMetadata WHERE Chave = $chave LIMIT 1";
        cmd.Parameters.AddWithValue("$chave", SchemaVersionKey);

        var raw = cmd.ExecuteScalar()?.ToString();
        return int.TryParse(raw, out var versao) && versao > 0 ? versao : 1;
    }

    private static void DefinirVersaoSchema(SqliteConnection connection, int versao)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO SchemaMetadata (Chave, Valor) VALUES ($chave, $valor)
ON CONFLICT(Chave) DO UPDATE SET Valor = excluded.Valor;
";
        cmd.Parameters.AddWithValue("$chave", SchemaVersionKey);
        cmd.Parameters.AddWithValue("$valor", versao.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao2(SqliteConnection connection)
    {
        EnsureGruposSchema(connection);
        EnsureClientesSchemaVersao4(connection);
    }

    private static void MigrarParaVersao3(SqliteConnection connection)
    {
        if (TemUnicidadeGlobalDocumento(connection))
        {
            RecriarTabelaClientesSemUnicidadeGlobal(connection);
        }

        EnsureGruposSchema(connection);
        EnsureClientesSchemaVersao4(connection);
        EnsureTarefasSchema(connection);
    }

    private static void MigrarParaVersao4(SqliteConnection connection)
    {
        EnsureGruposSchema(connection);
        RecriarTabelaClientesVersao4(connection);
        EnsureClientesSchemaVersao4(connection);
        EnsureTarefasSchema(connection);
    }

    private void MigrarParaVersao5(SqliteConnection connection)
    {
        var possuiDocumentoHash = TemColuna(connection, "Clientes", "DocumentoHash");
        var possuiDocumentoCipher = TemColuna(connection, "Clientes", "DocumentoCipher");
        if (possuiDocumentoHash && possuiDocumentoCipher)
            return;

        var possuiDocumentoLegado = TemColuna(connection, "Clientes", "Documento");
        if (!possuiDocumentoLegado)
            throw new InvalidOperationException("Migração v5 requer coluna legado Documento para conversão segura.");

        if (_dataProtector is null && !_allowLegacyPlaintext)
            throw new InvalidOperationException("Proteção de dados obrigatória: chave BYOK ausente para migrar clientes.");

        var possuiNomeFantasia = TemColuna(connection, "Clientes", "NomeFantasia");
        var possuiGrupoEmpresarialId = TemColuna(connection, "Clientes", "GrupoEmpresarialId");
        var possuiEmail = TemColuna(connection, "Clientes", "Email");
        var possuiTelefone = TemColuna(connection, "Clientes", "Telefone");

        var selectNomeFantasia = possuiNomeFantasia ? "NomeFantasia" : "NULL AS NomeFantasia";
        var selectGrupoEmpresarialId = possuiGrupoEmpresarialId ? "GrupoEmpresarialId" : "NULL AS GrupoEmpresarialId";
        var selectEmail = possuiEmail ? "Email" : "NULL AS Email";
        var selectTelefone = possuiTelefone ? "Telefone" : "NULL AS Telefone";

        var registros = new List<RegistroMigracaoClienteV5>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT Id, CodigoCliente, Nome, {selectNomeFantasia}, TipoDocumento, Documento, {selectGrupoEmpresarialId},
       {selectEmail}, {selectTelefone}, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc
FROM Clientes;
";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var documentoRaw = ReadNullableString(reader, "Documento");
                var documentoDigits = DocumentoClienteValidator.ExtrairSomenteDigitos(documentoRaw);
                if (string.IsNullOrWhiteSpace(documentoDigits))
                    throw new InvalidOperationException("Registro legado de cliente sem documento válido para migração.");

                var documentoHash = ComputeDocumentoHash(documentoDigits);
                var documentoCipher = EncryptSensitive(documentoDigits);
                var emailCipher = EncryptSensitiveNullable(ReadNullableString(reader, "Email"));
                var telefoneCipher = EncryptSensitiveNullable(ReadNullableString(reader, "Telefone"));

                registros.Add(new RegistroMigracaoClienteV5
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    CodigoCliente = NormalizarCodigoLegacy(ReadNullableString(reader, "CodigoCliente"), reader.GetInt32(reader.GetOrdinal("Id"))),
                    Nome = ReadNullableString(reader, "Nome") ?? string.Empty,
                    NomeFantasia = ReadNullableString(reader, "NomeFantasia"),
                    TipoDocumento = ReadNullableString(reader, "TipoDocumento") ?? "CNPJ",
                    DocumentoHash = documentoHash,
                    DocumentoCipher = documentoCipher,
                    GrupoEmpresarialId = ReadNullableInt(reader, "GrupoEmpresarialId"),
                    EmailCipher = emailCipher,
                    TelefoneCipher = telefoneCipher,
                    ProtecaoVersao = 1,
                    Ativo = ReadBoolSqlite(reader, "Ativo"),
                    CriadoPorUserId = ReadRequiredInt(reader, "CriadoPorUserId"),
                    CriadoEmUtc = ReadNullableString(reader, "CriadoEmUtc") ?? DateTime.UtcNow.ToString("o"),
                    AtualizadoEmUtc = ReadNullableString(reader, "AtualizadoEmUtc") ?? DateTime.UtcNow.ToString("o")
                });
            }
        }

        using var tx = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "PRAGMA foreign_keys = OFF;";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Clientes_v5 (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CodigoCliente TEXT NOT NULL,
  Nome TEXT NOT NULL,
  NomeFantasia TEXT NULL,
  TipoDocumento TEXT NOT NULL,
  DocumentoHash TEXT NOT NULL,
  DocumentoCipher TEXT NOT NULL,
  GrupoEmpresarialId INTEGER NULL,
  EmailCipher TEXT NULL,
  TelefoneCipher TEXT NULL,
  ProtecaoVersao INTEGER NOT NULL DEFAULT 1,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (GrupoEmpresarialId) REFERENCES GruposEmpresariais(Id)
);
";
                cmd.ExecuteNonQuery();
            }

            foreach (var registro in registros)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
INSERT INTO Clientes_v5 (
  Id, CodigoCliente, Nome, NomeFantasia, TipoDocumento, DocumentoHash, DocumentoCipher,
  GrupoEmpresarialId, EmailCipher, TelefoneCipher, ProtecaoVersao,
  Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc
)
VALUES (
  $id, $codigoCliente, $nome, $nomeFantasia, $tipoDocumento, $documentoHash, $documentoCipher,
  $grupoEmpresarialId, $emailCipher, $telefoneCipher, $protecaoVersao,
  $ativo, $criadoPorUserId, $criadoEmUtc, $atualizadoEmUtc
);
";
                insertCmd.Parameters.AddWithValue("$id", registro.Id);
                insertCmd.Parameters.AddWithValue("$codigoCliente", registro.CodigoCliente);
                insertCmd.Parameters.AddWithValue("$nome", registro.Nome);
                insertCmd.Parameters.AddWithValue("$nomeFantasia", (object?)registro.NomeFantasia ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("$tipoDocumento", registro.TipoDocumento);
                insertCmd.Parameters.AddWithValue("$documentoHash", registro.DocumentoHash);
                insertCmd.Parameters.AddWithValue("$documentoCipher", registro.DocumentoCipher);
                insertCmd.Parameters.AddWithValue("$grupoEmpresarialId", (object?)registro.GrupoEmpresarialId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("$emailCipher", (object?)registro.EmailCipher ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("$telefoneCipher", (object?)registro.TelefoneCipher ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("$protecaoVersao", registro.ProtecaoVersao);
                insertCmd.Parameters.AddWithValue("$ativo", registro.Ativo ? 1 : 0);
                insertCmd.Parameters.AddWithValue("$criadoPorUserId", registro.CriadoPorUserId);
                insertCmd.Parameters.AddWithValue("$criadoEmUtc", registro.CriadoEmUtc);
                insertCmd.Parameters.AddWithValue("$atualizadoEmUtc", registro.AtualizadoEmUtc);
                insertCmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
DROP TABLE Clientes;
ALTER TABLE Clientes_v5 RENAME TO Clientes;
PRAGMA foreign_keys = ON;
";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void MigrarParaVersao6(SqliteConnection connection)
    {
        if (!TemTabela(connection, "Tarefas"))
        {
            EnsureTarefasSchema(connection);
            return;
        }

        if (!TemColuna(connection, "Tarefas", "FerramentaId"))
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = @"
ALTER TABLE Tarefas
ADD COLUMN FerramentaId TEXT NOT NULL DEFAULT 'generica';
";
            alterCmd.ExecuteNonQuery();
        }

        EnsureTarefasSchema(connection);
    }

    private static void EnsureExecucaoSchema(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
-- Tabela de execucoes de ciclo (1 linha por ciclo disparado).
CREATE TABLE IF NOT EXISTS AncorarPdfExecucoes (
  ExecucaoId TEXT PRIMARY KEY,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  EsteiraId INTEGER NOT NULL,
  CicloId TEXT NOT NULL,
  JanelaAlvoUtc TEXT NOT NULL,
  IniciadaEmUtc TEXT NULL,
  FinalizadaEmUtc TEXT NULL,
  Status TEXT NOT NULL DEFAULT 'Agendada',
  ErroCodigo TEXT NULL,
  ErroDetalhe TEXT NULL,
  ExecutadaComAtraso INTEGER NOT NULL DEFAULT 0,
  ProgramadoPorUserId INTEGER NOT NULL,
  ProgramadoPorNome TEXT NOT NULL,
  ProgramadoEmUtc TEXT NOT NULL,
  ExecutadoPorUserId INTEGER NULL,
  CorrelationId TEXT NOT NULL DEFAULT '',
  CriadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

-- Tabela de resultados por variavel extraida (1..N por execucao).
CREATE TABLE IF NOT EXISTS AncorarPdfResultadosVariavel (
  ResultadoId TEXT PRIMARY KEY,
  ExecucaoId TEXT NOT NULL,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  ArquivoPath TEXT NOT NULL,
  ArquivoHash TEXT NOT NULL,
  Chave TEXT NOT NULL,
  ValorBruto TEXT NOT NULL,
  ValorNormalizado TEXT NOT NULL,
  Tipo TEXT NOT NULL DEFAULT 'texto',
  CorTemplate TEXT NOT NULL,
  Confianca REAL NOT NULL DEFAULT 0.0,
  Pagina INTEGER NOT NULL DEFAULT 1,
  BboxRelativoJson TEXT NULL,
  FOREIGN KEY (ExecucaoId) REFERENCES AncorarPdfExecucoes(ExecucaoId)
);

-- Tabela de saida consolidada por arquivo/ciclo (contrato de consumo futuro).
-- Nunca sobrescrever — sempre inserir novo registro por execucao.
CREATE TABLE IF NOT EXISTS AncorarPdfSaidaVariavel (
  SaidaId TEXT PRIMARY KEY,
  ExecucaoId TEXT NOT NULL,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  ArquivoPath TEXT NOT NULL,
  ArquivoHash TEXT NOT NULL,
  ArquivoNomeLogico TEXT NOT NULL,
  DataExecucaoUtc TEXT NOT NULL,
  SchemaVersion INTEGER NOT NULL DEFAULT 1,
  VariaveisJson TEXT NOT NULL,
  PayloadHashSha256 TEXT NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (ExecucaoId) REFERENCES AncorarPdfExecucoes(ExecucaoId)
);

-- Indices de leitura criticos (hot path do scheduler e consumo futuro).
CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucoes_Cliente_Status_Janela
  ON AncorarPdfExecucoes(ClienteId, Status, JanelaAlvoUtc DESC);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucoes_Tarefa_IniciadaEm
  ON AncorarPdfExecucoes(TarefaId, IniciadaEmUtc DESC);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfExecucoes_Tarefa_Ciclo
  ON AncorarPdfExecucoes(TarefaId, CicloId);

CREATE INDEX IF NOT EXISTS IX_AncorarPdfResultados_ExecucaoId
  ON AncorarPdfResultadosVariavel(ExecucaoId);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfResultados_Tarefa_ArquivoHash
  ON AncorarPdfResultadosVariavel(TarefaId, ArquivoHash);

CREATE INDEX IF NOT EXISTS IX_AncorarPdfSaida_Cliente_DataExecucao
  ON AncorarPdfSaidaVariavel(ClienteId, DataExecucaoUtc DESC);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSaida_Tarefa_DataExecucao
  ON AncorarPdfSaidaVariavel(TarefaId, DataExecucaoUtc DESC);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSaida_ExecucaoId
  ON AncorarPdfSaidaVariavel(ExecucaoId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSaida_SaidaId
  ON AncorarPdfSaidaVariavel(SaidaId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSaida_Tarefa_PayloadHash_V1
  ON AncorarPdfSaidaVariavel(TarefaId, PayloadHashSha256)
  WHERE SchemaVersion = 1;

-- Indice adicional de deduplicacao por hash de arquivo no ciclo
-- (complementa o indice unico existente em AncorarPdfArquivosProcessadosCiclo).
CREATE INDEX IF NOT EXISTS IX_AncorarPdf_ArquivoProcessadoCiclo_Hash
  ON AncorarPdfArquivosProcessadosCiclo(TarefaId, ArquivoHash, CicloId);
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao10(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AncorarPdfExecucaoFila (
    FilaItemId TEXT PRIMARY KEY,
    TarefaId INTEGER NOT NULL REFERENCES Tarefas(Id),
    ClienteId INTEGER NOT NULL,
    CicloId TEXT NOT NULL,
    JanelaAlvoUtc TEXT NOT NULL,
    PrioridadeExecucao INTEGER NOT NULL DEFAULT 3,
    Status TEXT NOT NULL DEFAULT 'Aguardando',
    Motivo TEXT NOT NULL DEFAULT 'scheduler_dispatch',
    EnfileiradoPorUserId INTEGER NOT NULL,
    EnfileiradoPorNome TEXT NOT NULL,
    EnfileiradoEmUtc TEXT NOT NULL,
    IniciadoEmUtc TEXT NULL,
    FinalizadoEmUtc TEXT NULL,
    TentativaAtual INTEGER NOT NULL DEFAULT 0,
    TentativasMaximas INTEGER NOT NULL DEFAULT 3,
    CategoriaFalha TEXT NULL,
    ErroCodigo TEXT NULL,
    ErroDetalhe TEXT NULL,
    CanceladoPorNome TEXT NULL,
    CanceladoEmUtc TEXT NULL,
    CorrelationId TEXT NOT NULL DEFAULT '',
    CriadoEmUtc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfExecucaoFila_Tarefa_Ciclo
    ON AncorarPdfExecucaoFila(TarefaId, CicloId)
    WHERE Status IN ('Aguardando','EmProcessamento');

CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucaoFila_Cliente_Status
    ON AncorarPdfExecucaoFila(ClienteId, Status, PrioridadeExecucao, EnfileiradoEmUtc);

CREATE TABLE IF NOT EXISTS AncorarPdfExecucaoLease (
    LeaseId TEXT PRIMARY KEY,
    FilaItemId TEXT NOT NULL REFERENCES AncorarPdfExecucaoFila(FilaItemId),
    TarefaId INTEGER NOT NULL,
    ClienteId INTEGER NOT NULL,
    WorkerId TEXT NOT NULL,
    AcquiredAtUtc TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    Ativa INTEGER NOT NULL DEFAULT 1,
    MotivoLiberacao TEXT NULL,
    LiberadaEmUtc TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfExecucaoLease_FilaItem_Ativa
    ON AncorarPdfExecucaoLease(FilaItemId) WHERE Ativa = 1;

CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucaoLease_Expirado
    ON AncorarPdfExecucaoLease(ExpiresAtUtc) WHERE Ativa = 1;
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao11(SqliteConnection connection)
    {
        // C7 — Adiciona campos de rastreabilidade ao registro de eventos do scheduler/motor.
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfSchedulerEventos", "CorrelationId",
            "TEXT NOT NULL DEFAULT ''");
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfSchedulerEventos", "StatusAnterior",
            "TEXT NOT NULL DEFAULT ''");
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfSchedulerEventos", "StatusNovo",
            "TEXT NOT NULL DEFAULT ''");
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfSchedulerEventos", "ErroCodigo",
            "TEXT NOT NULL DEFAULT ''");
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfSchedulerEventos", "ExecutadaComAtraso",
            "INTEGER NOT NULL DEFAULT 0");

        if (TemTabela(connection, "AncorarPdfSchedulerEventos"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSchedulerEventos_Cliente_Tipo
    ON AncorarPdfSchedulerEventos(ClienteId, TipoEvento, OcorreuEmUtc);

CREATE INDEX IF NOT EXISTS IX_AncorarPdfSchedulerEventos_Correlation
    ON AncorarPdfSchedulerEventos(CorrelationId)
    WHERE CorrelationId != '';
";
            cmd.ExecuteNonQuery();
        }
    }

    private static void AdicionarColunaSeNaoExiste(
        SqliteConnection connection, string tabela, string coluna, string definicao)
    {
        if (!TemTabela(connection, tabela))
            return;

        if (!TemColuna(connection, tabela, coluna))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {tabela} ADD COLUMN {coluna} {definicao};";
            cmd.ExecuteNonQuery();
        }
    }

    private static void MigrarParaVersao12(SqliteConnection connection)
    {
        // C10 — Controle de acesso hierárquico: Supremo > Admin > Usuario.
        AdicionarColunaSeNaoExiste(connection, "Users", "ResponsavelAdminId", "INTEGER NULL");
        AdicionarColunaSeNaoExiste(connection, "Users", "ExcluidoEmUtc", "TEXT NULL");

        if (TemTabela(connection, "Users"))
        {
            using var cmdIdx = connection.CreateCommand();
            cmdIdx.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_Users_ResponsavelAdminId ON Users(ResponsavelAdminId)
    WHERE ResponsavelAdminId IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Users_Status_Role ON Users(Status, Role);
";
            cmdIdx.ExecuteNonQuery();
        }

        if (TemTabela(connection, "Tarefas") && TemTabela(connection, "Users"))
        {
            using var cmdAudit = connection.CreateCommand();
            cmdAudit.CommandText = @"
CREATE TABLE IF NOT EXISTS TarefaAlteracoes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  AlteradoPorUserId INTEGER NOT NULL,
  AlteradoPorNome TEXT NOT NULL,
  CampoAlterado TEXT NOT NULL,
  ValorAnterior TEXT NULL,
  ValorNovo TEXT NULL,
  AlteradoEmUtc TEXT NOT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id),
  FOREIGN KEY (AlteradoPorUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_TarefaAlteracoes_TarefaId ON TarefaAlteracoes(TarefaId, AlteradoEmUtc DESC);
";
            cmdAudit.ExecuteNonQuery();
        }
    }

    private static void MigrarParaVersao13(SqliteConnection connection)
    {
        // C13 — Deduplicacao semantica: acelera BuscaPorHash em (TarefaId, PayloadHashSha256).
        EnsureExecucaoSchema(connection);
    }

    private static void MigrarParaVersao14(SqliteConnection connection)
    {
        // C14 — Deprecacao extrator_pdf: migra FerramentaId legado para ancorar_pdf.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Tarefas SET FerramentaId = 'ancorar_pdf' WHERE lower(trim(FerramentaId)) = 'extrator_pdf'";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao15(SqliteConnection connection)
    {
        // C15 — OCR fallback: colunas OcrFallbackAtivo, OcrDpi, OcrLang.
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfConfiguracoesTarefa", "OcrFallbackAtivo", "INTEGER NOT NULL DEFAULT 0");
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfConfiguracoesTarefa", "OcrDpi", "INTEGER NOT NULL DEFAULT 300");
        AdicionarColunaSeNaoExiste(connection, "AncorarPdfConfiguracoesTarefa", "OcrLang", "TEXT NOT NULL DEFAULT 'por+eng'");
    }

    private static void MigrarParaVersao16(SqliteConnection connection)
    {
        // C16 — Unicidade condicional de deduplicacao por hash no contrato de saida ativo (SchemaVersion=1).
        if (!TemTabela(connection, "AncorarPdfSaidaVariavel"))
        {
            EnsureExecucaoSchema(connection);
            return;
        }

        using var tx = connection.BeginTransaction();

        using (var cmdDedup = connection.CreateCommand())
        {
            cmdDedup.Transaction = tx;
            cmdDedup.CommandText = @"
DELETE FROM AncorarPdfSaidaVariavel
WHERE SchemaVersion = 1
  AND EXISTS (
    SELECT 1
    FROM AncorarPdfSaidaVariavel AS base
    WHERE base.SchemaVersion = 1
      AND base.TarefaId = AncorarPdfSaidaVariavel.TarefaId
      AND base.PayloadHashSha256 = AncorarPdfSaidaVariavel.PayloadHashSha256
      AND (
        base.CriadoEmUtc < AncorarPdfSaidaVariavel.CriadoEmUtc
        OR (base.CriadoEmUtc = AncorarPdfSaidaVariavel.CriadoEmUtc AND base.SaidaId < AncorarPdfSaidaVariavel.SaidaId)
      )
  );";
            cmdDedup.ExecuteNonQuery();
        }

        using (var cmdDrop = connection.CreateCommand())
        {
            cmdDrop.Transaction = tx;
            cmdDrop.CommandText = "DROP INDEX IF EXISTS IX_AncorarPdfSaida_Tarefa_PayloadHash;";
            cmdDrop.ExecuteNonQuery();
        }

        using (var cmdCreate = connection.CreateCommand())
        {
            cmdCreate.Transaction = tx;
            cmdCreate.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSaida_Tarefa_PayloadHash_V1
  ON AncorarPdfSaidaVariavel(TarefaId, PayloadHashSha256)
  WHERE SchemaVersion = 1;";
            cmdCreate.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void MigrarParaVersao17(SqliteConnection connection)
    {
        if (TemTabela(connection, "Esteiras"))
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Esteiras (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ClienteId INTEGER NOT NULL,
  Nome TEXT NOT NULL,
  Ordem INTEGER NOT NULL DEFAULT 0,
  Ativa INTEGER NOT NULL DEFAULT 1,
  CriadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (ClienteId) REFERENCES Clientes(Id)
);

CREATE INDEX IF NOT EXISTS IX_Esteiras_ClienteId ON Esteiras(ClienteId);
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao8(SqliteConnection connection)
    {
        EnsureExecucaoSchema(connection);
    }

    private static void MigrarParaVersao9(SqliteConnection connection)
    {
        if (!TemTabela(connection, "AncorarPdfConfiguracoesTarefa"))
        {
            EnsureTarefasSchema(connection);
            return;
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "TimezoneId"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN TimezoneId TEXT NOT NULL DEFAULT 'UTC';
";
            cmd.ExecuteNonQuery();
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "PrioridadeExecucao"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN PrioridadeExecucao INTEGER NOT NULL DEFAULT 3;
";
            cmd.ExecuteNonQuery();
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "DstHorarioInvalidoPolicy"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN DstHorarioInvalidoPolicy TEXT NOT NULL DEFAULT 'AvancarParaProximoHorarioValido';
";
            cmd.ExecuteNonQuery();
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "DstHorarioAmbiguoPolicy"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN DstHorarioAmbiguoPolicy TEXT NOT NULL DEFAULT 'PreferirOffsetMaisCedo';
";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AncorarPdfSchedulerBacklogPendente (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  JanelaAlvoUtc TEXT NOT NULL,
  AtrasoSegundos INTEGER NOT NULL,
  Motivo TEXT NOT NULL,
  DetectadoEmUtc TEXT NOT NULL,
  Status TEXT NOT NULL DEFAULT 'Pendente',
  DecididoEmUtc TEXT NULL,
  DecididoPorUserId INTEGER NULL,
  DecididoPorNome TEXT NULL,
  Observacao TEXT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE TABLE IF NOT EXISTS AncorarPdfSchedulerEventos (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  TipoEvento TEXT NOT NULL,
  Detalhes TEXT NULL,
  OcorreuEmUtc TEXT NOT NULL,
  FOREIGN KEY (TarefaId) REFERENCES Tarefas(Id)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSchedulerBacklog_Tarefa_Janela ON AncorarPdfSchedulerBacklogPendente(TarefaId, JanelaAlvoUtc);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSchedulerBacklog_Cliente_Status ON AncorarPdfSchedulerBacklogPendente(ClienteId, Status, DetectadoEmUtc);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfSchedulerEventos_Tarefa_Data ON AncorarPdfSchedulerEventos(TarefaId, OcorreuEmUtc);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfConfig_Prioridade ON AncorarPdfConfiguracoesTarefa(PrioridadeExecucao, TarefaId);
";
            cmd.ExecuteNonQuery();
        }

        EnsureTarefasSchema(connection);
    }

    private static void MigrarParaVersao7(SqliteConnection connection)
    {
        if (!TemTabela(connection, "AncorarPdfConfiguracoesTarefa"))
        {
            EnsureTarefasSchema(connection);
            return;
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "HighlightOpacity"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN HighlightOpacity REAL NOT NULL DEFAULT 0.40;
";
            cmd.ExecuteNonQuery();
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "ModoSelecao"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN ModoSelecao TEXT NOT NULL DEFAULT 'RetanguloLivre';
";
            cmd.ExecuteNonQuery();
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "PdfModeloCrossCliente"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN PdfModeloCrossCliente INTEGER NOT NULL DEFAULT 0;
";
            cmd.ExecuteNonQuery();
        }

        if (!TemColuna(connection, "AncorarPdfConfiguracoesTarefa", "PdfModeloCrossClienteJustificativa"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
ALTER TABLE AncorarPdfConfiguracoesTarefa
ADD COLUMN PdfModeloCrossClienteJustificativa TEXT NULL;
";
            cmd.ExecuteNonQuery();
        }

        EnsureTarefasSchema(connection);
    }

    private static bool TemTabela(SqliteConnection connection, string tabela)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = $tabela LIMIT 1";
        cmd.Parameters.AddWithValue("$tabela", tabela);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool TemUnicidadeGlobalDocumento(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA index_list('Clientes')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var unique = reader.GetInt32(2) == 1;
            var nomeIndice = reader.GetString(1);
            var partial = reader.FieldCount > 4 && !reader.IsDBNull(4) && reader.GetInt32(4) == 1;
            if (!unique || partial)
                continue;

            if (IndicePossuiColunaDocumento(connection, nomeIndice))
                return true;
        }

        return false;
    }

    private static bool IndicePossuiColunaDocumento(SqliteConnection connection, string nomeIndice)
    {
        using var cmd = connection.CreateCommand();
        var nomeIndiceEscapado = nomeIndice.Replace("'", "''", StringComparison.Ordinal);
        cmd.CommandText = $"PRAGMA index_info('{nomeIndiceEscapado}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.FieldCount > 2 && !reader.IsDBNull(2) && string.Equals(reader.GetString(2), "Documento", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void RecriarTabelaClientesSemUnicidadeGlobal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
PRAGMA foreign_keys = OFF;

CREATE TABLE IF NOT EXISTS Clientes_v3 (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Nome TEXT NOT NULL,
  TipoDocumento TEXT NOT NULL,
  Documento TEXT NOT NULL,
  Email TEXT NULL,
  Telefone TEXT NULL,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

INSERT INTO Clientes_v3 (Id, Nome, TipoDocumento, Documento, Email, Telefone, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc)
SELECT Id, Nome, TipoDocumento, Documento, Email, Telefone, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc
FROM Clientes;

DROP TABLE Clientes;
ALTER TABLE Clientes_v3 RENAME TO Clientes;

PRAGMA foreign_keys = ON;
";
        cmd.ExecuteNonQuery();
    }

    private static void RecriarTabelaClientesVersao4(SqliteConnection connection)
    {
        var temCodigoCliente = TemColuna(connection, "Clientes", "CodigoCliente");
        var temNomeFantasia = TemColuna(connection, "Clientes", "NomeFantasia");
        var temGrupoEmpresarialId = TemColuna(connection, "Clientes", "GrupoEmpresarialId");

        var exprCodigoCliente = temCodigoCliente
            ? "CASE WHEN CodigoCliente IS NULL OR TRIM(CodigoCliente) = '' THEN printf('LEGACY-%06d', Id) ELSE UPPER(TRIM(CodigoCliente)) END"
            : "printf('LEGACY-%06d', Id)";
        var exprNomeFantasia = temNomeFantasia ? "NomeFantasia" : "NULL";
        var exprGrupoEmpresarialId = temGrupoEmpresarialId ? "GrupoEmpresarialId" : "NULL";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
PRAGMA foreign_keys = OFF;

CREATE TABLE IF NOT EXISTS Clientes_v4 (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CodigoCliente TEXT NOT NULL,
  Nome TEXT NOT NULL,
  NomeFantasia TEXT NULL,
  TipoDocumento TEXT NOT NULL,
  Documento TEXT NOT NULL,
  GrupoEmpresarialId INTEGER NULL,
  Email TEXT NULL,
  Telefone TEXT NULL,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  FOREIGN KEY (GrupoEmpresarialId) REFERENCES GruposEmpresariais(Id)
);

INSERT INTO Clientes_v4 (
  Id, CodigoCliente, Nome, NomeFantasia, TipoDocumento, Documento, GrupoEmpresarialId,
  Email, Telefone, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc
)
SELECT
  Id,
  {exprCodigoCliente},
  Nome,
  {exprNomeFantasia},
  TipoDocumento,
  Documento,
  {exprGrupoEmpresarialId},
  Email,
  Telefone,
  Ativo,
  CriadoPorUserId,
  CriadoEmUtc,
  AtualizadoEmUtc
FROM Clientes;

DROP TABLE Clientes;
ALTER TABLE Clientes_v4 RENAME TO Clientes;

PRAGMA foreign_keys = ON;
";
        cmd.ExecuteNonQuery();
    }

    private static bool TemColuna(SqliteConnection connection, string tabela, string coluna)
    {
        using var cmd = connection.CreateCommand();
        var tabelaEscapada = tabela.Replace("'", "''", StringComparison.Ordinal);
        cmd.CommandText = $"PRAGMA table_info('{tabelaEscapada}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.FieldCount <= 1 || reader.IsDBNull(1))
                continue;

            if (string.Equals(reader.GetString(1), coluna, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool SchemaExists(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM AuditChain LIMIT 1";
        try
        {
            cmd.ExecuteScalar();
            return true;
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    private void ValidarCompatibilidadeProtecaoDados(SqliteConnection connection)
    {
        if (!TemColuna(connection, "Clientes", "DocumentoCipher"))
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT DocumentoCipher
FROM Clientes
WHERE DocumentoCipher IS NOT NULL
  AND TRIM(DocumentoCipher) <> ''
LIMIT 1;
";

        var cipherRaw = cmd.ExecuteScalar()?.ToString();
        if (string.IsNullOrWhiteSpace(cipherRaw))
            return;

        if (cipherRaw.StartsWith("legacy:", StringComparison.Ordinal))
            return;

        if (_dataProtector is null)
        {
            throw new InvalidOperationException(
                "Esta base contém dados protegidos por chave BYOK e não pode ser aberta sem chave BYOK válida.");
        }

        try
        {
            var documento = _dataProtector.Decrypt(cipherRaw);
            var digits = DocumentoClienteValidator.ExtrairSomenteDigitos(documento);
            if (digits.Length != 11 && digits.Length != 14)
                throw new InvalidOperationException("Documento de verificação inválido após decriptação.");
        }
        catch (Exception ex) when (ex is CryptographicException || ex is InvalidOperationException || ex is FormatException || ex is ArgumentException)
        {
            throw new InvalidOperationException(
                "Chave BYOK incompatível com os dados existentes. Use a mesma chave da instalação original.",
                ex);
        }
    }

    private string CriarBackup(SqliteConnection connection)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var backupPath = $"{DbPath}.bak.{timestamp}";
        var escaped = backupPath.Replace("'", "''", StringComparison.Ordinal);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"VACUUM main INTO '{escaped}';";
        cmd.ExecuteNonQuery();
        return backupPath;
    }

    private void TentarRestaurarBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
            return;

        File.Copy(backupPath, DbPath, overwrite: true);
    }

    private string ComputeDocumentoHash(string documentoSomenteDigitos)
    {
        var digits = DocumentoClienteValidator.ExtrairSomenteDigitos(documentoSomenteDigitos);
        if (string.IsNullOrWhiteSpace(digits))
            throw new InvalidOperationException("Documento inválido para hash.");

        if (_dataProtector is not null)
            return _dataProtector.ComputeDocumentoHash(digits);

        if (_allowLegacyPlaintext)
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(digits)));

        throw new InvalidOperationException("Proteção de dados obrigatória sem chave BYOK válida.");
    }

    private string EncryptSensitive(string plainText)
    {
        if (_dataProtector is not null)
            return _dataProtector.Encrypt(plainText);

        if (_allowLegacyPlaintext)
            return $"legacy:{Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText))}";

        throw new InvalidOperationException("Proteção de dados obrigatória sem chave BYOK válida.");
    }

    private string? EncryptSensitiveNullable(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return null;

        return EncryptSensitive(plainText.Trim());
    }

    private static string NormalizarCodigoLegacy(string? codigoCliente, int id)
    {
        if (string.IsNullOrWhiteSpace(codigoCliente))
            return $"LEGACY-{id:000000}";

        var normalized = codigoCliente.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? $"LEGACY-{id:000000}"
            : normalized;
    }

    private static int ReadRequiredInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return 0;

        return reader.GetInt32(ordinal);
    }

    private static bool ReadBoolSqlite(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return false;

        return reader.GetInt32(ordinal) == 1;
    }

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private sealed class RegistroMigracaoClienteV5
    {
        public int Id { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string? NomeFantasia { get; set; }
        public string TipoDocumento { get; set; } = "CNPJ";
        public string DocumentoHash { get; set; } = string.Empty;
        public string DocumentoCipher { get; set; } = string.Empty;
        public int? GrupoEmpresarialId { get; set; }
        public string? EmailCipher { get; set; }
        public string? TelefoneCipher { get; set; }
        public int ProtecaoVersao { get; set; }
        public bool Ativo { get; set; }
        public int CriadoPorUserId { get; set; }
        public string CriadoEmUtc { get; set; } = string.Empty;
        public string AtualizadoEmUtc { get; set; } = string.Empty;
    }
}
