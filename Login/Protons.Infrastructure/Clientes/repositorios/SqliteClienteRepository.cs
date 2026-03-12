using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Protons.Core.Clientes.Exceptions;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Clientes.Security;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Clientes.Repositories;

public sealed class SqliteClienteRepository : IClienteRepository
{
    private readonly SqliteDb _db;
    private readonly IClienteDataProtector? _dataProtector;
    private readonly bool _allowLegacyPlaintext;

    public SqliteClienteRepository(SqliteDb db, IClienteDataProtector? dataProtector = null, bool allowLegacyPlaintext = true)
    {
        _db = db;
        _dataProtector = dataProtector;
        _allowLegacyPlaintext = allowLegacyPlaintext;

        if (_dataProtector is null && !_allowLegacyPlaintext)
            throw new InvalidOperationException("Proteção de dados obrigatória: repositório de clientes sem chave BYOK válida.");
    }

    public Cliente? GetByDocumento(string? documentoSomenteDigitos)
    {
        var documentoDigits = SomenteDigitos(documentoSomenteDigitos ?? string.Empty);
        if (string.IsNullOrWhiteSpace(documentoDigits))
            return null;

        var documentoHash = ComputeDocumentoHash(documentoDigits);

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.Nome AS GrupoEmpresarialNome
FROM Clientes c
LEFT JOIN GruposEmpresariais g ON g.Id = c.GrupoEmpresarialId
WHERE c.DocumentoHash = $documentoHash AND c.Ativo = 1
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$documentoHash", documentoHash);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Cliente? GetByCodigoCliente(string? codigoCliente)
    {
        if (string.IsNullOrWhiteSpace(codigoCliente))
            return null;

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.Nome AS GrupoEmpresarialNome
FROM Clientes c
LEFT JOIN GruposEmpresariais g ON g.Id = c.GrupoEmpresarialId
WHERE UPPER(c.CodigoCliente) = UPPER($codigoCliente) AND c.Ativo = 1
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$codigoCliente", codigoCliente.Trim());

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Cliente? GetById(int id)
    {
        if (id <= 0)
            return null;

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.Nome AS GrupoEmpresarialNome
FROM Clientes c
LEFT JOIN GruposEmpresariais g ON g.Id = c.GrupoEmpresarialId
WHERE c.Id = $id AND c.Ativo = 1
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Cliente> ListarTodos()
    {
        var list = new List<Cliente>();

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.Nome AS GrupoEmpresarialNome
FROM Clientes c
LEFT JOIN GruposEmpresariais g ON g.Id = c.GrupoEmpresarialId
WHERE c.Ativo = 1
ORDER BY c.Nome COLLATE NOCASE ASC;
";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public IReadOnlyList<Cliente> Buscar(string? termo, int pagina, int tamanhoPagina)
    {
        var paginaNormalizada = pagina < 1 ? 1 : pagina;
        var tamanhoNormalizado = tamanhoPagina <= 0 ? 20 : tamanhoPagina;
        var offset = (paginaNormalizada - 1) * tamanhoNormalizado;

        var termoNormalizado = string.IsNullOrWhiteSpace(termo) ? null : termo.Trim();
        var termoUpper = termoNormalizado?.ToUpperInvariant();
        var termoNome = termoNormalizado is null ? null : $"%{termoNormalizado}%";
        var termoPrefixo = termoNormalizado is null ? null : $"{termoNormalizado}%";
        var termoCodigo = termoUpper is null ? null : $"%{termoUpper}%";
        var termoCodigoPrefixo = termoUpper is null ? null : $"{termoUpper}%";

        var termoDocumentoExato = SomenteDigitos(termoNormalizado ?? string.Empty);
        if (termoDocumentoExato.Length != 11 && termoDocumentoExato.Length != 14)
            termoDocumentoExato = null;

        var termoDocumentoHash = termoDocumentoExato is null ? null : ComputeDocumentoHash(termoDocumentoExato);

        var list = new List<Cliente>();

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.Nome AS GrupoEmpresarialNome
FROM Clientes c
LEFT JOIN GruposEmpresariais g ON g.Id = c.GrupoEmpresarialId
WHERE c.Ativo = 1
  AND (
    $termoVazio = 1
    OR c.Nome LIKE $termoNome COLLATE NOCASE
    OR c.NomeFantasia LIKE $termoNome COLLATE NOCASE
    OR c.CodigoCliente LIKE $termoCodigo COLLATE NOCASE
    OR c.DocumentoHash = $termoDocumentoHash
    OR g.Nome LIKE $termoNome COLLATE NOCASE
  )
ORDER BY
  CASE
    WHEN $termoVazio = 1 THEN 100
    WHEN c.CodigoCliente = $termoCodigoExato THEN 0
    WHEN c.DocumentoHash = $termoDocumentoHash THEN 1
    WHEN c.Nome = $termoExato COLLATE NOCASE THEN 2
    WHEN c.NomeFantasia = $termoExato COLLATE NOCASE THEN 3
    WHEN g.Nome = $termoExato COLLATE NOCASE THEN 4
    WHEN c.CodigoCliente LIKE $termoCodigoPrefixo COLLATE NOCASE THEN 5
    WHEN c.Nome LIKE $termoPrefixo COLLATE NOCASE THEN 6
    WHEN c.NomeFantasia LIKE $termoPrefixo COLLATE NOCASE THEN 7
    WHEN g.Nome LIKE $termoPrefixo COLLATE NOCASE THEN 8
    ELSE 20
  END,
  c.Nome COLLATE NOCASE ASC,
  c.Id ASC
LIMIT $limit OFFSET $offset;
";
        cmd.Parameters.AddWithValue("$termoVazio", termoNormalizado is null ? 1 : 0);
        cmd.Parameters.AddWithValue("$termoNome", (object?)termoNome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoPrefixo", (object?)termoPrefixo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoCodigo", (object?)termoCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoCodigoExato", (object?)termoUpper ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoCodigoPrefixo", (object?)termoCodigoPrefixo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoDocumentoHash", (object?)termoDocumentoHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoExato", (object?)termoNormalizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", tamanhoNormalizado);
        cmd.Parameters.AddWithValue("$offset", offset);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public int Contar(string? termo)
    {
        var termoNormalizado = string.IsNullOrWhiteSpace(termo) ? null : termo.Trim();
        var termoUpper = termoNormalizado?.ToUpperInvariant();
        var termoNome = termoNormalizado is null ? null : $"%{termoNormalizado}%";
        var termoCodigo = termoUpper is null ? null : $"%{termoUpper}%";

        var termoDocumentoExato = SomenteDigitos(termoNormalizado ?? string.Empty);
        if (termoDocumentoExato.Length != 11 && termoDocumentoExato.Length != 14)
            termoDocumentoExato = null;

        var termoDocumentoHash = termoDocumentoExato is null ? null : ComputeDocumentoHash(termoDocumentoExato);

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(1)
FROM Clientes c
LEFT JOIN GruposEmpresariais g ON g.Id = c.GrupoEmpresarialId
WHERE c.Ativo = 1
  AND (
    $termoVazio = 1
    OR c.Nome LIKE $termoNome COLLATE NOCASE
    OR c.NomeFantasia LIKE $termoNome COLLATE NOCASE
    OR c.CodigoCliente LIKE $termoCodigo COLLATE NOCASE
    OR c.DocumentoHash = $termoDocumentoHash
    OR g.Nome LIKE $termoNome COLLATE NOCASE
  );
";
        cmd.Parameters.AddWithValue("$termoVazio", termoNormalizado is null ? 1 : 0);
        cmd.Parameters.AddWithValue("$termoNome", (object?)termoNome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoCodigo", (object?)termoCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$termoDocumentoHash", (object?)termoDocumentoHash ?? DBNull.Value);

        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
            return 0;

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public int Create(Cliente cliente)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Clientes (
  CodigoCliente, Nome, NomeFantasia, TipoDocumento, DocumentoHash, DocumentoCipher, GrupoEmpresarialId,
  EmailCipher, TelefoneCipher, ProtecaoVersao, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc
)
VALUES (
  $codigoCliente, $nome, $nomeFantasia, $tipoDocumento, $documentoHash, $documentoCipher, $grupoEmpresarialId,
  $emailCipher, $telefoneCipher, $protecaoVersao, $ativo, $criadoPor, $criadoEmUtc, $atualizadoEmUtc
);
SELECT last_insert_rowid();
";
        BindCliente(cmd, cliente);

        object? result;
        try
        {
            result = cmd.ExecuteScalar();
        }
        catch (SqliteException ex) when (EhErroDuplicidadeCodigoCliente(ex))
        {
            throw new CodigoClienteDuplicadoException("Código de cliente duplicado.", ex);
        }
        catch (SqliteException ex) when (EhErroDuplicidadeDocumentoCliente(ex))
        {
            throw new DocumentoClienteDuplicadoException("Documento de cliente duplicado.", ex);
        }
        catch (SqliteException ex) when (EhErroUnicidade(ex))
        {
            throw new DocumentoClienteDuplicadoException("Documento ou código de cliente duplicado.", ex);
        }

        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException("Create cliente failed: no identity returned.");

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public void Update(Cliente cliente)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Clientes SET
  CodigoCliente = $codigoCliente,
  Nome = $nome,
  NomeFantasia = $nomeFantasia,
  TipoDocumento = $tipoDocumento,
  DocumentoHash = $documentoHash,
  DocumentoCipher = $documentoCipher,
  GrupoEmpresarialId = $grupoEmpresarialId,
  EmailCipher = $emailCipher,
  TelefoneCipher = $telefoneCipher,
  ProtecaoVersao = $protecaoVersao,
  Ativo = $ativo,
  AtualizadoEmUtc = $atualizadoEmUtc
WHERE Id = $id;
";
        BindCliente(cmd, cliente);
        cmd.Parameters.AddWithValue("$id", cliente.Id);
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (EhErroDuplicidadeCodigoCliente(ex))
        {
            throw new CodigoClienteDuplicadoException("Código de cliente duplicado.", ex);
        }
        catch (SqliteException ex) when (EhErroDuplicidadeDocumentoCliente(ex))
        {
            throw new DocumentoClienteDuplicadoException("Documento de cliente duplicado.", ex);
        }
    }

    public void Inativar(int clienteId, int atualizadoPorUserId, DateTime atualizadoEmUtc)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Clientes
SET Ativo = 0,
    AtualizadoEmUtc = $atualizadoEmUtc
WHERE Id = $id;
";
        cmd.Parameters.AddWithValue("$id", clienteId);
        cmd.Parameters.AddWithValue("$atualizadoEmUtc", atualizadoEmUtc.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public GrupoEmpresarial? GetGrupoById(int grupoId)
    {
        if (grupoId <= 0)
            return null;

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM GruposEmpresariais WHERE Id = $id AND Ativo = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("$id", grupoId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapGrupo(reader) : null;
    }

    public GrupoEmpresarial? GetGrupoByNomeNormalizado(string? nomeNormalizado)
    {
        if (string.IsNullOrWhiteSpace(nomeNormalizado))
            return null;

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM GruposEmpresariais WHERE NomeNormalizado = $nomeNormalizado AND Ativo = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("$nomeNormalizado", nomeNormalizado.Trim().ToUpperInvariant());

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapGrupo(reader) : null;
    }

    public IReadOnlyList<GrupoEmpresarial> ListarGrupos()
    {
        var list = new List<GrupoEmpresarial>();

        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM GruposEmpresariais
WHERE Ativo = 1
ORDER BY Nome COLLATE NOCASE ASC;
";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapGrupo(reader));
        }

        return list;
    }

    public int CreateGrupo(GrupoEmpresarial grupo)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO GruposEmpresariais (Nome, NomeNormalizado, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc)
VALUES ($nome, $nomeNormalizado, $ativo, $criadoPor, $criadoEmUtc, $atualizadoEmUtc);
SELECT last_insert_rowid();
";
        cmd.Parameters.AddWithValue("$nome", grupo.Nome);
        cmd.Parameters.AddWithValue("$nomeNormalizado", grupo.NomeNormalizado);
        cmd.Parameters.AddWithValue("$ativo", grupo.Ativo ? 1 : 0);
        cmd.Parameters.AddWithValue("$criadoPor", grupo.CriadoPorUserId);
        cmd.Parameters.AddWithValue("$criadoEmUtc", grupo.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$atualizadoEmUtc", grupo.AtualizadoEmUtc.ToString("o"));

        object? result;
        try
        {
            result = cmd.ExecuteScalar();
        }
        catch (SqliteException ex) when (EhErroUnicidade(ex))
        {
            throw new DocumentoClienteDuplicadoException("Grupo empresarial duplicado.", ex);
        }

        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException("Create grupo failed: no identity returned.");

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private void BindCliente(SqliteCommand cmd, Cliente cliente)
    {
        var documentoDigits = SomenteDigitos(cliente.Documento);
        if (string.IsNullOrWhiteSpace(documentoDigits))
            throw new InvalidOperationException("Documento do cliente inválido para persistência.");

        cmd.Parameters.AddWithValue("$codigoCliente", cliente.CodigoCliente);
        cmd.Parameters.AddWithValue("$nome", cliente.Nome);
        cmd.Parameters.AddWithValue("$nomeFantasia", (object?)cliente.NomeFantasia ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tipoDocumento", cliente.TipoDocumento.ToString());
        cmd.Parameters.AddWithValue("$documentoHash", ComputeDocumentoHash(documentoDigits));
        cmd.Parameters.AddWithValue("$documentoCipher", EncryptSensitive(documentoDigits));
        cmd.Parameters.AddWithValue("$grupoEmpresarialId", (object?)cliente.GrupoEmpresarialId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$emailCipher", (object?)EncryptSensitiveNullable(cliente.Email) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$telefoneCipher", (object?)EncryptSensitiveNullable(cliente.Telefone) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$protecaoVersao", 1);
        cmd.Parameters.AddWithValue("$ativo", cliente.Ativo ? 1 : 0);
        cmd.Parameters.AddWithValue("$criadoPor", cliente.CriadoPorUserId);
        cmd.Parameters.AddWithValue("$criadoEmUtc", cliente.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$atualizadoEmUtc", cliente.AtualizadoEmUtc.ToString("o"));
    }

    private Cliente Map(SqliteDataReader reader)
    {
        return new Cliente
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            CodigoCliente = reader.GetString(reader.GetOrdinal("CodigoCliente")),
            Nome = reader.GetString(reader.GetOrdinal("Nome")),
            NomeFantasia = ReadNullableString(reader, "NomeFantasia"),
            TipoDocumento = Enum.TryParse<TipoDocumentoCliente>(reader.GetString(reader.GetOrdinal("TipoDocumento")), true, out var tipo)
                ? tipo
                : TipoDocumentoCliente.CNPJ,
            Documento = DecryptSensitive(reader.GetString(reader.GetOrdinal("DocumentoCipher"))),
            GrupoEmpresarialId = ReadNullableInt(reader, "GrupoEmpresarialId"),
            GrupoEmpresarialNome = ReadNullableString(reader, "GrupoEmpresarialNome"),
            Email = DecryptSensitiveNullable(ReadNullableString(reader, "EmailCipher")),
            Telefone = DecryptSensitiveNullable(ReadNullableString(reader, "TelefoneCipher")),
            Ativo = reader.GetInt32(reader.GetOrdinal("Ativo")) == 1,
            CriadoPorUserId = reader.GetInt32(reader.GetOrdinal("CriadoPorUserId")),
            CriadoEmUtc = ReadRequiredDate(reader, "CriadoEmUtc"),
            AtualizadoEmUtc = ReadRequiredDate(reader, "AtualizadoEmUtc")
        };
    }

    private static GrupoEmpresarial MapGrupo(SqliteDataReader reader)
    {
        return new GrupoEmpresarial
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Nome = reader.GetString(reader.GetOrdinal("Nome")),
            NomeNormalizado = reader.GetString(reader.GetOrdinal("NomeNormalizado")),
            Ativo = reader.GetInt32(reader.GetOrdinal("Ativo")) == 1,
            CriadoPorUserId = reader.GetInt32(reader.GetOrdinal("CriadoPorUserId")),
            CriadoEmUtc = ReadRequiredDate(reader, "CriadoEmUtc"),
            AtualizadoEmUtc = ReadRequiredDate(reader, "AtualizadoEmUtc")
        };
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

    private static DateTime ReadRequiredDate(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return DateTime.UtcNow;

        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
    }

    private string ComputeDocumentoHash(string documentoSomenteDigitos)
    {
        var digits = SomenteDigitos(documentoSomenteDigitos);
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

    private string DecryptSensitive(string cipherText)
    {
        if (cipherText.StartsWith("legacy:", StringComparison.Ordinal))
        {
            var raw = cipherText["legacy:".Length..];
            var bytes = Convert.FromBase64String(raw);
            return Encoding.UTF8.GetString(bytes);
        }

        if (_dataProtector is not null)
            return _dataProtector.Decrypt(cipherText);

        if (_allowLegacyPlaintext)
            return cipherText;

        throw new InvalidOperationException("Proteção de dados obrigatória sem chave BYOK válida.");
    }

    private string? DecryptSensitiveNullable(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return null;

        return DecryptSensitive(cipherText);
    }

    private static string SomenteDigitos(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static bool EhErroUnicidade(SqliteException ex)
    {
        return ex.SqliteErrorCode == 19 &&
               ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EhErroDuplicidadeCodigoCliente(SqliteException ex)
    {
        if (!EhErroUnicidade(ex))
            return false;

        return ex.Message.Contains("UX_Clientes_CodigoCliente", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("Clientes.CodigoCliente", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EhErroDuplicidadeDocumentoCliente(SqliteException ex)
    {
        if (!EhErroUnicidade(ex))
            return false;

        return ex.Message.Contains("UX_Clientes_DocumentoHash_Ativo", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("Clientes.DocumentoHash", StringComparison.OrdinalIgnoreCase);
    }
}
