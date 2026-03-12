using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Protons.Infrastructure.Clientes.Security;
using Protons.Infrastructure.Login.Database;
using Xunit;

namespace Protons.Infrastructure.Tests.Database;

public sealed class SqliteDbMigrationTests
{
    [Fact]
    public void EnsureCreated_DeveMigrarSchemaLegadoParaVersao5ComBackupEProtecaoDeDados()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_schema_test_{Guid.NewGuid():N}.db");
        var protector = CriarProtector();

        try
        {
            CriarSchemaV4ComCliente(dbPath);

            var db = new SqliteDb(dbPath, protector, allowLegacyPlaintext: false);
            db.EnsureCreated();

            var backups = Directory.GetFiles(
                Path.GetDirectoryName(dbPath) ?? Path.GetTempPath(),
                Path.GetFileName(dbPath) + ".bak.*");
            backups.Should().NotBeEmpty();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            using var versaoCmd = connection.CreateCommand();
            versaoCmd.CommandText = "SELECT Valor FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            var versao = versaoCmd.ExecuteScalar()?.ToString();
            int.TryParse(versao, out var versaoNumerica).Should().BeTrue("schema deve retornar versao numerica");
            versaoNumerica.Should().BeGreaterThanOrEqualTo(10, "migracao deve entregar schema moderno");

            using var tableInfo = connection.CreateCommand();
            tableInfo.CommandText = "PRAGMA table_info('Clientes')";
            using var readerInfo = tableInfo.ExecuteReader();
            var temDocumentoHash = false;
            var temDocumentoCipher = false;
            var temEmailCipher = false;
            var temTelefoneCipher = false;
            var temDocumentoLegado = false;

            while (readerInfo.Read())
            {
                if (readerInfo.IsDBNull(1))
                    continue;

                var nome = readerInfo.GetString(1);
                if (string.Equals(nome, "DocumentoHash", StringComparison.OrdinalIgnoreCase)) temDocumentoHash = true;
                if (string.Equals(nome, "DocumentoCipher", StringComparison.OrdinalIgnoreCase)) temDocumentoCipher = true;
                if (string.Equals(nome, "EmailCipher", StringComparison.OrdinalIgnoreCase)) temEmailCipher = true;
                if (string.Equals(nome, "TelefoneCipher", StringComparison.OrdinalIgnoreCase)) temTelefoneCipher = true;
                if (string.Equals(nome, "Documento", StringComparison.OrdinalIgnoreCase)) temDocumentoLegado = true;
            }

            temDocumentoHash.Should().BeTrue();
            temDocumentoCipher.Should().BeTrue();
            temEmailCipher.Should().BeTrue();
            temTelefoneCipher.Should().BeTrue();
            temDocumentoLegado.Should().BeFalse();

            using var idxHashCmd = connection.CreateCommand();
            idxHashCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_Clientes_DocumentoHash'";
            idxHashCmd.ExecuteScalar()?.ToString().Should().Be("IX_Clientes_DocumentoHash");

            using var idxUniqueCmd = connection.CreateCommand();
            idxUniqueCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name='UX_Clientes_DocumentoHash_Ativo'";
            idxUniqueCmd.ExecuteScalar()?.ToString().Should().Be("UX_Clientes_DocumentoHash_Ativo");

            using var dadosCmd = connection.CreateCommand();
            dadosCmd.CommandText = @"
SELECT CodigoCliente, DocumentoHash, DocumentoCipher, EmailCipher, TelefoneCipher
FROM Clientes
WHERE Id = 1";

            using var dadosReader = dadosCmd.ExecuteReader();
            dadosReader.Read().Should().BeTrue();

            var codigo = dadosReader.GetString(0);
            var documentoHash = dadosReader.GetString(1);
            var documentoCipher = dadosReader.GetString(2);
            var emailCipher = dadosReader.GetString(3);
            var telefoneCipher = dadosReader.GetString(4);

            codigo.Should().Be("CLI-LEGADO");
            documentoHash.Should().NotBeNullOrWhiteSpace();
            documentoCipher.Should().StartWith("v1:");
            emailCipher.Should().StartWith("v1:");
            telefoneCipher.Should().StartWith("v1:");

            protector.Decrypt(documentoCipher).Should().Be("04252011000110");
            protector.Decrypt(emailCipher).Should().Be("legado@empresa.teste");
            protector.Decrypt(telefoneCipher).Should().Be("11999999999");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void EnsureCreated_DeveFalharFechado_QuandoChaveByokAusenteNaMigracao()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_schema_test_{Guid.NewGuid():N}.db");

        try
        {
            CriarSchemaV4ComCliente(dbPath);

            var db = new SqliteDb(dbPath, dataProtector: null, allowLegacyPlaintext: false);
            var act = () => db.EnsureCreated();

            act.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Contain("Proteção de dados obrigatória");

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            using var versaoCmd = connection.CreateCommand();
            versaoCmd.CommandText = "SELECT Valor FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            var versao = versaoCmd.ExecuteScalar()?.ToString();
            versao.Should().Be("4");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void EnsureCreated_DeveFalharFechado_QuandoChaveByokNaoCorrespondeAosDadosExistentes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_schema_test_{Guid.NewGuid():N}.db");
        var protectorCorreto = CriarProtector(1);
        var protectorIncorreto = CriarProtector(33);

        try
        {
            CriarSchemaV4ComCliente(dbPath);

            var dbComChaveCorreta = new SqliteDb(dbPath, protectorCorreto, allowLegacyPlaintext: false);
            dbComChaveCorreta.EnsureCreated();

            var dbComChaveIncorreta = new SqliteDb(dbPath, protectorIncorreto, allowLegacyPlaintext: false);
            var act = () => dbComChaveIncorreta.EnsureCreated();

            act.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Contain("incompatível");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void EnsureCreated_DeveFalharFechado_QuandoBaseCriptografadaAbreSemChaveMesmoComLegacyLigado()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_schema_test_{Guid.NewGuid():N}.db");
        var protector = CriarProtector(1);

        try
        {
            CriarSchemaV4ComCliente(dbPath);

            var dbComChave = new SqliteDb(dbPath, protector, allowLegacyPlaintext: false);
            dbComChave.EnsureCreated();

            var dbSemChave = new SqliteDb(dbPath, dataProtector: null, allowLegacyPlaintext: true);
            var act = () => dbSemChave.EnsureCreated();

            act.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Contain("não pode ser aberta sem chave BYOK");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static void CriarSchemaV4ComCliente(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? ".");

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AuditChain (
  Id INTEGER PRIMARY KEY CHECK (Id = 1),
  LastHash TEXT NULL
);
INSERT OR IGNORE INTO AuditChain (Id, LastHash) VALUES (1, NULL);

CREATE TABLE IF NOT EXISTS SchemaMetadata (
  Chave TEXT PRIMARY KEY,
  Valor TEXT NOT NULL
);
INSERT OR REPLACE INTO SchemaMetadata (Chave, Valor) VALUES ('SchemaVersion', '4');

CREATE TABLE IF NOT EXISTS GruposEmpresariais (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Nome TEXT NOT NULL,
  NomeNormalizado TEXT NOT NULL,
  Ativo INTEGER NOT NULL DEFAULT 1,
  CriadoPorUserId INTEGER NOT NULL,
  CriadoEmUtc TEXT NOT NULL,
  AtualizadoEmUtc TEXT NOT NULL
);

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
  AtualizadoEmUtc TEXT NOT NULL
);

INSERT INTO Clientes (
  Id, CodigoCliente, Nome, NomeFantasia, TipoDocumento, Documento,
  GrupoEmpresarialId, Email, Telefone, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc
)
VALUES (
  1, 'CLI-LEGADO', 'Empresa Legado', 'Fantasia Legado', 'CNPJ', '04.252.011/0001-10',
  NULL, 'legado@empresa.teste', '11999999999', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'
);
";
        cmd.ExecuteNonQuery();
    }

    private static AesGcmClienteDataProtector CriarProtector(byte seed = 1)
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
            key[i] = (byte)(seed + i);

        return new AesGcmClienteDataProtector(key);
    }
}
