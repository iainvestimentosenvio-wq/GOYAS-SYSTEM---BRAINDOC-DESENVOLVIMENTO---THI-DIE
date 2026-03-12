using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Protons.Core.Clientes.Security;
using Protons.Core.Clientes.Validation;

namespace Protons.Infrastructure.Login.Database;

public sealed class PostgresDb
{
    private const string SchemaVersionKey = "SchemaVersion";
    private const int SchemaVersionAtual = 16;

    private readonly IClienteDataProtector? _dataProtector;
    private readonly bool _allowLegacyPlaintext;

    public string ConnectionString { get; }

    public PostgresDb(string connectionString, IClienteDataProtector? dataProtector = null, bool allowLegacyPlaintext = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
        _dataProtector = dataProtector;
        _allowLegacyPlaintext = allowLegacyPlaintext;
    }

    public void EnsureCreated()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
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
            EnsureSchemaMetadataTable(connection);
            ValidarCompatibilidadeProtecaoDados(connection);
            DefinirVersaoSchema(connection, SchemaVersionAtual);
            return;
        }

        EnsureSchemaMetadataTable(connection);
        var versao = ObterVersaoSchema(connection);
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

        EnsureGruposSchema(connection);
        EnsureClientesSchema(connection);
        EnsureTarefasSchema(connection);
        ValidarCompatibilidadeProtecaoDados(connection);
        DefinirVersaoSchema(connection, SchemaVersionAtual);
    }

    private static void CriarSchemaBase(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
  Id SERIAL PRIMARY KEY,
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
  Id SERIAL PRIMARY KEY,
  TimestampUtc TEXT NOT NULL,
  UserId INTEGER NULL REFERENCES Users(Id),
  EmailSnapshot TEXT NULL,
  Acao TEXT NOT NULL,
  Resultado TEXT NOT NULL,
  Detalhes TEXT NULL,
  Maquina TEXT NOT NULL,
  VersaoApp TEXT NOT NULL,
  PrevHash TEXT NULL,
  Hash TEXT NULL
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

INSERT INTO AuditChain (Id, LastHash) VALUES (1, NULL)
ON CONFLICT (Id) DO NOTHING;
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureClientesSchema(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Clientes (
  Id SERIAL PRIMARY KEY,
  CodigoCliente TEXT NOT NULL,
  Nome TEXT NOT NULL,
  NomeFantasia TEXT NULL,
  TipoDocumento TEXT NOT NULL,
  DocumentoHash TEXT NOT NULL,
  DocumentoCipher TEXT NOT NULL,
  GrupoEmpresarialId INTEGER NULL REFERENCES GruposEmpresariais(Id),
  EmailCipher TEXT NULL,
  TelefoneCipher TEXT NULL,
  ProtecaoVersao INTEGER NOT NULL DEFAULT 1,
  Ativo BOOLEAN NOT NULL DEFAULT TRUE,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

DROP INDEX IF EXISTS ix_clientes_documento;
DROP INDEX IF EXISTS ux_clientes_documento_ativo;

CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_CodigoCliente ON Clientes(CodigoCliente);
CREATE INDEX IF NOT EXISTS IX_Clientes_Nome ON Clientes(Nome);
CREATE INDEX IF NOT EXISTS IX_Clientes_NomeFantasia ON Clientes(NomeFantasia);
CREATE INDEX IF NOT EXISTS IX_Clientes_Ativo ON Clientes(Ativo);
CREATE INDEX IF NOT EXISTS IX_Clientes_GrupoEmpresarialId ON Clientes(GrupoEmpresarialId);
CREATE INDEX IF NOT EXISTS IX_Clientes_DocumentoHash ON Clientes(DocumentoHash);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_DocumentoHash_Ativo ON Clientes(DocumentoHash) WHERE Ativo = TRUE;
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureClientesSchemaVersao4(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Clientes (
  Id SERIAL PRIMARY KEY,
  CodigoCliente TEXT NOT NULL,
  Nome TEXT NOT NULL,
  NomeFantasia TEXT NULL,
  TipoDocumento TEXT NOT NULL,
  Documento TEXT NOT NULL,
  GrupoEmpresarialId INTEGER NULL REFERENCES GruposEmpresariais(Id),
  Email TEXT NULL,
  Telefone TEXT NULL,
  Ativo BOOLEAN NOT NULL DEFAULT TRUE,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_CodigoCliente ON Clientes(CodigoCliente);
CREATE INDEX IF NOT EXISTS IX_Clientes_Nome ON Clientes(Nome);
CREATE INDEX IF NOT EXISTS IX_Clientes_NomeFantasia ON Clientes(NomeFantasia);
CREATE INDEX IF NOT EXISTS IX_Clientes_Ativo ON Clientes(Ativo);
CREATE INDEX IF NOT EXISTS IX_Clientes_Documento ON Clientes(Documento);
CREATE INDEX IF NOT EXISTS IX_Clientes_GrupoEmpresarialId ON Clientes(GrupoEmpresarialId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Clientes_Documento_Ativo ON Clientes(Documento) WHERE Ativo = TRUE;
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureGruposSchema(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS GruposEmpresariais (
  Id SERIAL PRIMARY KEY,
  Nome TEXT NOT NULL,
  NomeNormalizado TEXT NOT NULL,
  Ativo BOOLEAN NOT NULL DEFAULT TRUE,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_GruposEmpresariais_Nome ON GruposEmpresariais(Nome);
CREATE UNIQUE INDEX IF NOT EXISTS UX_GruposEmpresariais_NomeNormalizado ON GruposEmpresariais(NomeNormalizado);
";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureTarefasSchema(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Tarefas (
  Id SERIAL PRIMARY KEY,
  ClienteId INTEGER NOT NULL REFERENCES Clientes(Id),
  FerramentaId TEXT NOT NULL DEFAULT 'generica',
  Titulo TEXT NOT NULL,
  VencimentoUtc TEXT NOT NULL,
  ResponsavelUserId INTEGER NOT NULL REFERENCES Users(Id),
  Status TEXT NOT NULL,
  Recorrencia TEXT NOT NULL,
  DiaRecorrenciaMensal INTEGER NULL,
  Ativa BOOLEAN NOT NULL DEFAULT TRUE,
  CriadoPorUserId INTEGER NOT NULL REFERENCES Users(Id),
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  ConcluidaEmUtc TEXT NULL
);

CREATE TABLE IF NOT EXISTS ClientePermissoesUsuarios (
  Id SERIAL PRIMARY KEY,
  ClienteId INTEGER NOT NULL REFERENCES Clientes(Id),
  UserId INTEGER NOT NULL REFERENCES Users(Id),
  PodeEditar BOOLEAN NOT NULL DEFAULT FALSE,
  ConcedidoPorUserId INTEGER NOT NULL REFERENCES Users(Id),
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AncorarPdfArquivosProcessadosCiclo (
  Id SERIAL PRIMARY KEY,
  TarefaId INTEGER NOT NULL REFERENCES Tarefas(Id),
  CicloId TEXT NOT NULL,
  NomeEsperadoLogico TEXT NOT NULL,
  ArquivoHash TEXT NOT NULL,
  ArquivoPath TEXT NOT NULL,
  TamanhoBytes BIGINT NOT NULL,
  MtimeUtc TEXT NOT NULL,
  ProcessadoEmUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AncorarPdfConfiguracoesTarefa (
  TarefaId INTEGER PRIMARY KEY REFERENCES Tarefas(Id),
  ClienteId INTEGER NOT NULL,
  EsteiraId INTEGER NOT NULL,
  NomeTarefaPersonalizado TEXT NOT NULL,
  PastaMonitoradaPath TEXT NOT NULL,
  PdfModeloPath TEXT NOT NULL,
  NomeReferenciaArquivo TEXT NOT NULL,
  MonitorarSubpastas BOOLEAN NOT NULL DEFAULT FALSE,
  ValidacaoClienteAtiva BOOLEAN NOT NULL DEFAULT TRUE,
  LimiarSimilaridadeNome DOUBLE PRECISION NOT NULL DEFAULT 0.75,
  HighlightOpacity DOUBLE PRECISION NOT NULL DEFAULT 0.40,
  ModoSelecao TEXT NOT NULL DEFAULT 'RetanguloLivre',
  PdfModeloCrossCliente BOOLEAN NOT NULL DEFAULT FALSE,
  PdfModeloCrossClienteJustificativa TEXT NULL,
  Recorrencia TEXT NOT NULL,
  AgendamentoSegundo INTEGER NOT NULL DEFAULT 0,
  TimezoneId TEXT NOT NULL DEFAULT 'UTC',
  PrioridadeExecucao INTEGER NOT NULL DEFAULT 3,
  DstHorarioInvalidoPolicy TEXT NOT NULL DEFAULT 'AvancarParaProximoHorarioValido',
  DstHorarioAmbiguoPolicy TEXT NOT NULL DEFAULT 'PreferirOffsetMaisCedo',
  TemplateAncorasJson TEXT NOT NULL,
  OcrFallbackAtivo BOOLEAN NOT NULL DEFAULT FALSE,
  OcrDpi INTEGER NOT NULL DEFAULT 300,
  OcrLang TEXT NOT NULL DEFAULT 'por+eng',
  ProgramadoPorUserId INTEGER NOT NULL,
  ProgramadoPorNome TEXT NOT NULL,
  ProgramadoEmUtc TEXT NOT NULL,
  AtualizadoPorUserId INTEGER NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL,
  VersaoTemplate INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS AncorarPdfTemplateHistorico (
  Id SERIAL PRIMARY KEY,
  TarefaId INTEGER NOT NULL REFERENCES Tarefas(Id),
  Versao INTEGER NOT NULL,
  AntesJson TEXT NOT NULL,
  DepoisJson TEXT NOT NULL,
  AlteradoPorUserId INTEGER NOT NULL,
  AlteradoPorNome TEXT NOT NULL,
  AlteradoEmUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AncorarPdfSchedulerBacklogPendente (
  Id BIGSERIAL PRIMARY KEY,
  TarefaId INTEGER NOT NULL REFERENCES Tarefas(Id),
  ClienteId INTEGER NOT NULL,
  JanelaAlvoUtc TEXT NOT NULL,
  AtrasoSegundos INTEGER NOT NULL,
  Motivo TEXT NOT NULL,
  DetectadoEmUtc TEXT NOT NULL,
  Status TEXT NOT NULL DEFAULT 'Pendente',
  DecididoEmUtc TEXT NULL,
  DecididoPorUserId INTEGER NULL,
  DecididoPorNome TEXT NULL,
  Observacao TEXT NULL
);

CREATE TABLE IF NOT EXISTS AncorarPdfSchedulerEventos (
  Id BIGSERIAL PRIMARY KEY,
  TarefaId INTEGER NOT NULL REFERENCES Tarefas(Id),
  ClienteId INTEGER NOT NULL,
  TipoEvento TEXT NOT NULL,
  Detalhes TEXT NULL,
  OcorreuEmUtc TEXT NOT NULL
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

    private static void EnsureSchemaMetadataTable(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SchemaMetadata (
  Chave TEXT PRIMARY KEY,
  Valor TEXT NOT NULL
);
";
        cmd.ExecuteNonQuery();
    }

    private static int ObterVersaoSchema(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Valor FROM SchemaMetadata WHERE Chave = @chave LIMIT 1";
        cmd.Parameters.AddWithValue("@chave", SchemaVersionKey);

        var raw = cmd.ExecuteScalar()?.ToString();
        return int.TryParse(raw, out var versao) && versao > 0 ? versao : 1;
    }

    private static void DefinirVersaoSchema(NpgsqlConnection connection, int versao)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO SchemaMetadata (Chave, Valor) VALUES (@chave, @valor)
ON CONFLICT (Chave) DO UPDATE SET Valor = EXCLUDED.Valor;
";
        cmd.Parameters.AddWithValue("@chave", SchemaVersionKey);
        cmd.Parameters.AddWithValue("@valor", versao.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao2(NpgsqlConnection connection)
    {
        EnsureGruposSchema(connection);
        EnsureClientesSchemaVersao4(connection);
    }

    private static void MigrarParaVersao3(NpgsqlConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
ALTER TABLE Clientes DROP CONSTRAINT IF EXISTS clientes_documento_key;
DROP INDEX IF EXISTS ux_clientes_documento_ativo;
";
            cmd.ExecuteNonQuery();
        }

        EnsureGruposSchema(connection);
        EnsureClientesSchemaVersao4(connection);
        EnsureTarefasSchema(connection);
    }

    private static void MigrarParaVersao4(NpgsqlConnection connection)
    {
        EnsureGruposSchema(connection);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
ALTER TABLE Clientes ADD COLUMN IF NOT EXISTS CodigoCliente TEXT;
ALTER TABLE Clientes ADD COLUMN IF NOT EXISTS NomeFantasia TEXT NULL;
ALTER TABLE Clientes ADD COLUMN IF NOT EXISTS GrupoEmpresarialId INTEGER NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_clientes_grupoempresarial'
    ) THEN
        ALTER TABLE Clientes
        ADD CONSTRAINT fk_clientes_grupoempresarial
        FOREIGN KEY (GrupoEmpresarialId) REFERENCES GruposEmpresariais(Id);
    END IF;
END $$;

UPDATE Clientes
SET CodigoCliente = CONCAT('LEGACY-', LPAD(Id::text, 6, '0'))
WHERE CodigoCliente IS NULL OR BTRIM(CodigoCliente) = '';

UPDATE Clientes
SET CodigoCliente = UPPER(BTRIM(CodigoCliente))
WHERE CodigoCliente IS NOT NULL;

ALTER TABLE Clientes
ALTER COLUMN CodigoCliente SET NOT NULL;
";
            cmd.ExecuteNonQuery();
        }

        EnsureClientesSchemaVersao4(connection);
        EnsureTarefasSchema(connection);
    }

    private void MigrarParaVersao5(NpgsqlConnection connection)
    {
        var possuiDocumentoHash = TemColuna(connection, "clientes", "documentohash");
        var possuiDocumentoCipher = TemColuna(connection, "clientes", "documentocipher");
        if (possuiDocumentoHash && possuiDocumentoCipher)
            return;

        var possuiDocumentoLegado = TemColuna(connection, "clientes", "documento");
        if (!possuiDocumentoLegado)
            throw new InvalidOperationException("Migração v5 requer coluna legado documento para conversão.");

        if (_dataProtector is null && !_allowLegacyPlaintext)
            throw new InvalidOperationException("Proteção de dados obrigatória: chave BYOK ausente para migrar clientes.");

        using var tx = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
ALTER TABLE clientes ADD COLUMN IF NOT EXISTS documentohash TEXT;
ALTER TABLE clientes ADD COLUMN IF NOT EXISTS documentocipher TEXT;
ALTER TABLE clientes ADD COLUMN IF NOT EXISTS emailcipher TEXT NULL;
ALTER TABLE clientes ADD COLUMN IF NOT EXISTS telefonecipher TEXT NULL;
ALTER TABLE clientes ADD COLUMN IF NOT EXISTS protecaoversao INTEGER NOT NULL DEFAULT 1;
";
                cmd.ExecuteNonQuery();
            }

            var possuiEmailLegado = TemColuna(connection, "clientes", "email", tx);
            var possuiTelefoneLegado = TemColuna(connection, "clientes", "telefone", tx);
            var selectEmail = possuiEmailLegado ? "email" : "NULL::text AS email";
            var selectTelefone = possuiTelefoneLegado ? "telefone" : "NULL::text AS telefone";

            var registros = new List<RegistroMigracaoClienteV5>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
SELECT id, documento, {selectEmail}, {selectTelefone}
FROM clientes
WHERE documentohash IS NULL OR documentocipher IS NULL;
";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var documentoRaw = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var documentoDigits = DocumentoClienteValidator.ExtrairSomenteDigitos(documentoRaw);
                    if (string.IsNullOrWhiteSpace(documentoDigits))
                        throw new InvalidOperationException("Registro legado de cliente sem documento válido para migração.");

                    var email = possuiEmailLegado && !reader.IsDBNull(2) ? reader.GetString(2) : null;
                    var telefone = possuiTelefoneLegado && !reader.IsDBNull(3) ? reader.GetString(3) : null;

                    registros.Add(new RegistroMigracaoClienteV5
                    {
                        Id = reader.GetInt32(0),
                        DocumentoHash = ComputeDocumentoHash(documentoDigits),
                        DocumentoCipher = EncryptSensitive(documentoDigits),
                        EmailCipher = EncryptSensitiveNullable(email),
                        TelefoneCipher = EncryptSensitiveNullable(telefone)
                    });
                }
            }

            foreach (var registro in registros)
            {
                using var update = connection.CreateCommand();
                update.Transaction = tx;
                update.CommandText = @"
UPDATE clientes
SET documentohash = @hash,
    documentocipher = @cipher,
    emailcipher = @emailCipher,
    telefonecipher = @telefoneCipher,
    protecaoversao = 1
WHERE id = @id;
";
                update.Parameters.AddWithValue("@hash", registro.DocumentoHash);
                update.Parameters.AddWithValue("@cipher", registro.DocumentoCipher);
                update.Parameters.AddWithValue("@emailCipher", (object?)registro.EmailCipher ?? DBNull.Value);
                update.Parameters.AddWithValue("@telefoneCipher", (object?)registro.TelefoneCipher ?? DBNull.Value);
                update.Parameters.AddWithValue("@id", registro.Id);
                update.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
ALTER TABLE clientes ALTER COLUMN documentohash SET NOT NULL;
ALTER TABLE clientes ALTER COLUMN documentocipher SET NOT NULL;

ALTER TABLE clientes DROP CONSTRAINT IF EXISTS clientes_documento_key;
DROP INDEX IF EXISTS ux_clientes_documento_ativo;
DROP INDEX IF EXISTS ix_clientes_documento;
";
                cmd.ExecuteNonQuery();
            }

            if (possuiDocumentoLegado)
            {
                using var clearDocumento = connection.CreateCommand();
                clearDocumento.Transaction = tx;
                clearDocumento.CommandText = @"
ALTER TABLE clientes ALTER COLUMN documento DROP NOT NULL;
UPDATE clientes SET documento = NULL;
";
                clearDocumento.ExecuteNonQuery();
            }

            if (possuiEmailLegado)
            {
                using var clearEmail = connection.CreateCommand();
                clearEmail.Transaction = tx;
                clearEmail.CommandText = "UPDATE clientes SET email = NULL;";
                clearEmail.ExecuteNonQuery();
            }

            if (possuiTelefoneLegado)
            {
                using var clearTelefone = connection.CreateCommand();
                clearTelefone.Transaction = tx;
                clearTelefone.CommandText = "UPDATE clientes SET telefone = NULL;";
                clearTelefone.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void MigrarParaVersao6(NpgsqlConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
ALTER TABLE tarefas ADD COLUMN IF NOT EXISTS ferramentaid TEXT NOT NULL DEFAULT 'generica';
";
            cmd.ExecuteNonQuery();
        }

        EnsureTarefasSchema(connection);
    }

    private static void EnsureExecucaoSchema(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
-- Tabela de execucoes de ciclo (1 linha por ciclo disparado).
CREATE TABLE IF NOT EXISTS AncorarPdfExecucoes (
  ExecucaoId TEXT PRIMARY KEY,
  TarefaId INTEGER NOT NULL REFERENCES Tarefas(Id),
  ClienteId INTEGER NOT NULL,
  EsteiraId INTEGER NOT NULL,
  CicloId TEXT NOT NULL,
  JanelaAlvoUtc TEXT NOT NULL,
  IniciadaEmUtc TEXT NULL,
  FinalizadaEmUtc TEXT NULL,
  Status TEXT NOT NULL DEFAULT 'Agendada',
  ErroCodigo TEXT NULL,
  ErroDetalhe TEXT NULL,
  ExecutadaComAtraso BOOLEAN NOT NULL DEFAULT FALSE,
  ProgramadoPorUserId INTEGER NOT NULL,
  ProgramadoPorNome TEXT NOT NULL,
  ProgramadoEmUtc TEXT NOT NULL,
  ExecutadoPorUserId INTEGER NULL,
  CorrelationId TEXT NOT NULL DEFAULT '',
  CriadoEmUtc TEXT NOT NULL
);

-- Tabela de resultados por variavel extraida (1..N por execucao).
CREATE TABLE IF NOT EXISTS AncorarPdfResultadosVariavel (
  ResultadoId TEXT PRIMARY KEY,
  ExecucaoId TEXT NOT NULL REFERENCES AncorarPdfExecucoes(ExecucaoId),
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  ArquivoPath TEXT NOT NULL,
  ArquivoHash TEXT NOT NULL,
  Chave TEXT NOT NULL,
  ValorBruto TEXT NOT NULL,
  ValorNormalizado TEXT NOT NULL,
  Tipo TEXT NOT NULL DEFAULT 'texto',
  CorTemplate TEXT NOT NULL,
  Confianca DOUBLE PRECISION NOT NULL DEFAULT 0.0,
  Pagina INTEGER NOT NULL DEFAULT 1,
  BboxRelativoJson TEXT NULL
);

-- Tabela de saida consolidada por arquivo/ciclo (contrato de consumo futuro).
-- Nunca sobrescrever — sempre inserir novo registro por execucao.
CREATE TABLE IF NOT EXISTS AncorarPdfSaidaVariavel (
  SaidaId TEXT PRIMARY KEY,
  ExecucaoId TEXT NOT NULL REFERENCES AncorarPdfExecucoes(ExecucaoId),
  TarefaId INTEGER NOT NULL,
  ClienteId INTEGER NOT NULL,
  ArquivoPath TEXT NOT NULL,
  ArquivoHash TEXT NOT NULL,
  ArquivoNomeLogico TEXT NOT NULL,
  DataExecucaoUtc TEXT NOT NULL,
  SchemaVersion INTEGER NOT NULL DEFAULT 1,
  VariaveisJson TEXT NOT NULL,
  PayloadHashSha256 TEXT NOT NULL,
  CriadoEmUtc TEXT NOT NULL
);

-- Indices de leitura criticos (hot path do scheduler e consumo futuro).
CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucoes_Cliente_Status_Janela
  ON AncorarPdfExecucoes(ClienteId, Status, JanelaAlvoUtc DESC);
CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucoes_Tarefa_IniciadaEm
  ON AncorarPdfExecucoes(TarefaId, IniciadaEmUtc DESC);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfExecucoes_Tarefa_Ciclo
  ON AncorarPdfExecucoes(TarefaId, CicloId);

-- Indice parcial Postgres para hot path de status pendentes (Agendada/Executando).
-- Reduz custo de escrita comparado a indice total em tabelas grandes.
CREATE INDEX IF NOT EXISTS IX_AncorarPdfExecucoes_Pendentes
  ON AncorarPdfExecucoes(ClienteId, JanelaAlvoUtc)
  WHERE Status IN ('Agendada', 'Executando');

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
CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSaida_Tarefa_PayloadHash_V1
  ON AncorarPdfSaidaVariavel(TarefaId, PayloadHashSha256)
  WHERE SchemaVersion = 1;

-- Indice adicional de deduplicacao por hash de arquivo no ciclo.
CREATE INDEX IF NOT EXISTS IX_AncorarPdf_ArquivoProcessadoCiclo_Hash
  ON AncorarPdfArquivosProcessadosCiclo(TarefaId, ArquivoHash, CicloId);
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao10(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS ancorarpdfexecucaofila (
    filaitemid TEXT PRIMARY KEY,
    tarefaid INTEGER NOT NULL REFERENCES tarefas(id),
    clienteid INTEGER NOT NULL,
    cicloid TEXT NOT NULL,
    janelaalvoutc TEXT NOT NULL,
    prioridadeexecucao INTEGER NOT NULL DEFAULT 3,
    status TEXT NOT NULL DEFAULT 'Aguardando',
    motivo TEXT NOT NULL DEFAULT 'scheduler_dispatch',
    enfileiradoporuserid INTEGER NOT NULL,
    enfileiradopornome TEXT NOT NULL,
    enfileiradoemutc TEXT NOT NULL,
    iniciadoemutc TEXT NULL,
    finalizadoemutc TEXT NULL,
    tentativaatual INTEGER NOT NULL DEFAULT 0,
    tentativasmaximas INTEGER NOT NULL DEFAULT 3,
    categoriafalha TEXT NULL,
    errocodigo TEXT NULL,
    errodetalhe TEXT NULL,
    canceladopornome TEXT NULL,
    canceladoemutc TEXT NULL,
    correlationid TEXT NOT NULL DEFAULT '',
    criadoemutc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ancorarpdfexecucaofila_tarefa_ciclo
    ON ancorarpdfexecucaofila(tarefaid, cicloid)
    WHERE status IN ('Aguardando','EmProcessamento');

CREATE INDEX IF NOT EXISTS ix_ancorarpdfexecucaofila_cliente_status
    ON ancorarpdfexecucaofila(clienteid, status, prioridadeexecucao, enfileiradoemutc);

CREATE TABLE IF NOT EXISTS ancorarpdfexecucaolease (
    leaseid TEXT PRIMARY KEY,
    filaitemid TEXT NOT NULL REFERENCES ancorarpdfexecucaofila(filaitemid),
    tarefaid INTEGER NOT NULL,
    clienteid INTEGER NOT NULL,
    workerid TEXT NOT NULL,
    acquiredatutc TEXT NOT NULL,
    expiresatutc TEXT NOT NULL,
    ativa BOOLEAN NOT NULL DEFAULT TRUE,
    motivoliberacao TEXT NULL,
    liberadaemutc TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ancorarpdfexecucaolease_filaitem_ativa
    ON ancorarpdfexecucaolease(filaitemid) WHERE ativa = TRUE;

CREATE INDEX IF NOT EXISTS ix_ancorarpdfexecucaolease_expirado
    ON ancorarpdfexecucaolease(expiresatutc) WHERE ativa = TRUE;
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao11(NpgsqlConnection connection)
    {
        // C7 — Adiciona campos de rastreabilidade ao registro de eventos do scheduler/motor.
        // Postgres suporta ADD COLUMN IF NOT EXISTS nativamente.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
ALTER TABLE ancorarpdfschedulereventos ADD COLUMN IF NOT EXISTS correlationid TEXT NOT NULL DEFAULT '';
ALTER TABLE ancorarpdfschedulereventos ADD COLUMN IF NOT EXISTS statusanterior TEXT NOT NULL DEFAULT '';
ALTER TABLE ancorarpdfschedulereventos ADD COLUMN IF NOT EXISTS statusnovo TEXT NOT NULL DEFAULT '';
ALTER TABLE ancorarpdfschedulereventos ADD COLUMN IF NOT EXISTS errocodigo TEXT NOT NULL DEFAULT '';
ALTER TABLE ancorarpdfschedulereventos ADD COLUMN IF NOT EXISTS executadacomatraso BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS ix_ancorarpdfschedulereventos_cliente_tipo
    ON ancorarpdfschedulereventos(clienteid, tipoevento, ocorreuemutc DESC);

CREATE INDEX IF NOT EXISTS ix_ancorarpdfschedulereventos_correlation
    ON ancorarpdfschedulereventos(correlationid)
    WHERE correlationid != '';
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao12(NpgsqlConnection connection)
    {
        // C10 — Controle de acesso hierárquico: Supremo > Admin > Usuario.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
ALTER TABLE users ADD COLUMN IF NOT EXISTS responsaveladminid INTEGER NULL REFERENCES users(id);
ALTER TABLE users ADD COLUMN IF NOT EXISTS excluidoemutc TIMESTAMP WITH TIME ZONE NULL;

CREATE INDEX IF NOT EXISTS ix_users_responsaveladminid ON users(responsaveladminid)
    WHERE responsaveladminid IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_status_role ON users(status, role);

CREATE TABLE IF NOT EXISTS tarefaalteracoes (
  id SERIAL PRIMARY KEY,
  tarefaid INTEGER NOT NULL REFERENCES tarefas(id),
  alteradoporuserid INTEGER NOT NULL REFERENCES users(id),
  alteradopornome TEXT NOT NULL,
  campoalterado TEXT NOT NULL,
  valoranterior TEXT NULL,
  valornovo TEXT NULL,
  alteradoemutc TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_tarefaalteracoes_tarefaid ON tarefaalteracoes(tarefaid, alteradoemutc DESC);
";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao13(NpgsqlConnection connection)
    {
        // C13 — Deduplicacao semantica: acelera BuscaPorHash em (TarefaId, PayloadHashSha256).
        EnsureExecucaoSchema(connection);
    }

    private static void MigrarParaVersao14(NpgsqlConnection connection)
    {
        // C14 — Deprecacao extrator_pdf: migra FerramentaId legado para ancorar_pdf.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE tarefas SET ferramentaid = 'ancorar_pdf' WHERE lower(trim(ferramentaid)) = 'extrator_pdf'";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao15(NpgsqlConnection connection)
    {
        // C15 — OCR fallback: colunas OcrFallbackAtivo, OcrDpi, OcrLang.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS ocrfallbackativo BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS ocrdpi INTEGER NOT NULL DEFAULT 300;
ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS ocrlang TEXT NOT NULL DEFAULT 'por+eng';";
            cmd.ExecuteNonQuery();
        }
    }

    private static void MigrarParaVersao16(NpgsqlConnection connection)
    {
        // C16 — Unicidade condicional de deduplicacao por hash no contrato de saida ativo (SchemaVersion=1).
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
DELETE FROM AncorarPdfSaidaVariavel AS alvo
USING (
    SELECT SaidaId
    FROM (
        SELECT
            SaidaId,
            ROW_NUMBER() OVER (
                PARTITION BY TarefaId, PayloadHashSha256
                ORDER BY CriadoEmUtc, SaidaId
            ) AS rn
        FROM AncorarPdfSaidaVariavel
        WHERE SchemaVersion = 1
    ) ranked
    WHERE rn > 1
) duplicados
WHERE alvo.SaidaId = duplicados.SaidaId;

DROP INDEX IF EXISTS IX_AncorarPdfSaida_Tarefa_PayloadHash;

CREATE UNIQUE INDEX IF NOT EXISTS UX_AncorarPdfSaida_Tarefa_PayloadHash_V1
  ON AncorarPdfSaidaVariavel(TarefaId, PayloadHashSha256)
  WHERE SchemaVersion = 1;";
        cmd.ExecuteNonQuery();
    }

    private static void MigrarParaVersao8(NpgsqlConnection connection)
    {
        EnsureExecucaoSchema(connection);
    }

    private static void MigrarParaVersao9(NpgsqlConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
DO $$
BEGIN
    IF to_regclass('ancorarpdfconfiguracoestarefa') IS NOT NULL THEN
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS timezoneid TEXT NOT NULL DEFAULT 'UTC';
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS prioridadeexecucao INTEGER NOT NULL DEFAULT 3;
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS dsthorarioinvalidopolicy TEXT NOT NULL DEFAULT 'AvancarParaProximoHorarioValido';
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS dsthorarioambiguopolicy TEXT NOT NULL DEFAULT 'PreferirOffsetMaisCedo';
    END IF;
END $$;
";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS ancorarpdfschedulerbacklogpendente (
  id BIGSERIAL PRIMARY KEY,
  tarefaid INTEGER NOT NULL REFERENCES tarefas(id),
  clienteid INTEGER NOT NULL,
  janelaalvoutc TEXT NOT NULL,
  atrasosegundos INTEGER NOT NULL,
  motivo TEXT NOT NULL,
  detectadoemutc TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'Pendente',
  decididoemutc TEXT NULL,
  decididoporuserid INTEGER NULL,
  decididopornome TEXT NULL,
  observacao TEXT NULL
);

CREATE TABLE IF NOT EXISTS ancorarpdfschedulereventos (
  id BIGSERIAL PRIMARY KEY,
  tarefaid INTEGER NOT NULL REFERENCES tarefas(id),
  clienteid INTEGER NOT NULL,
  tipoevento TEXT NOT NULL,
  detalhes TEXT NULL,
  ocorreuemutc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ancorarpdfschedulerbacklog_tarefa_janela
  ON ancorarpdfschedulerbacklogpendente(tarefaid, janelaalvoutc);
CREATE INDEX IF NOT EXISTS ix_ancorarpdfschedulerbacklog_cliente_status
  ON ancorarpdfschedulerbacklogpendente(clienteid, status, detectadoemutc);
CREATE INDEX IF NOT EXISTS ix_ancorarpdfschedulereventos_tarefa_data
  ON ancorarpdfschedulereventos(tarefaid, ocorreuemutc);
CREATE INDEX IF NOT EXISTS ix_ancorarpdfconfig_prioridade
  ON ancorarpdfconfiguracoestarefa(prioridadeexecucao, tarefaid);
";
            cmd.ExecuteNonQuery();
        }

        EnsureTarefasSchema(connection);
    }

    private static void MigrarParaVersao7(NpgsqlConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
DO $$
BEGIN
    IF to_regclass('ancorarpdfconfiguracoestarefa') IS NOT NULL THEN
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS highlightopacity DOUBLE PRECISION NOT NULL DEFAULT 0.40;
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS modoselecao TEXT NOT NULL DEFAULT 'RetanguloLivre';
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS pdfmodelocrosscliente BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE ancorarpdfconfiguracoestarefa ADD COLUMN IF NOT EXISTS pdfmodelocrossclientejustificativa TEXT NULL;
    END IF;
END $$;
";
            cmd.ExecuteNonQuery();
        }

        EnsureTarefasSchema(connection);
    }

    private static bool TemColuna(NpgsqlConnection connection, string tabela, string coluna, NpgsqlTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT 1
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name = @tabela
  AND column_name = @coluna
LIMIT 1;
";
        cmd.Parameters.AddWithValue("@tabela", tabela.ToLowerInvariant());
        cmd.Parameters.AddWithValue("@coluna", coluna.ToLowerInvariant());

        var result = cmd.ExecuteScalar();
        return result is not null && result != DBNull.Value;
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

    private static bool SchemaExists(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM AuditChain LIMIT 1";
        try
        {
            cmd.ExecuteScalar();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return false;
        }
    }

    private void ValidarCompatibilidadeProtecaoDados(NpgsqlConnection connection)
    {
        if (!TemColuna(connection, "clientes", "documentocipher"))
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT documentocipher
FROM clientes
WHERE documentocipher IS NOT NULL
  AND BTRIM(documentocipher) <> ''
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

    public NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private sealed class RegistroMigracaoClienteV5
    {
        public int Id { get; set; }
        public string DocumentoHash { get; set; } = string.Empty;
        public string DocumentoCipher { get; set; } = string.Empty;
        public string? EmailCipher { get; set; }
        public string? TelefoneCipher { get; set; }
    }
}
