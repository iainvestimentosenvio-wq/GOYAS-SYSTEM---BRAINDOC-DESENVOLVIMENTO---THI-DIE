using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Protons.Core.Clientes.Exceptions;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Clientes.Security;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Clientes.Repositories;

public sealed class PostgresClienteRepository : IClienteRepository
{
    private readonly PostgresDb _db;
    private readonly IClienteDataProtector? _dataProtector;
    private readonly bool _allowLegacyPlaintext;

    public PostgresClienteRepository(PostgresDb db, IClienteDataProtector? dataProtector = null, bool allowLegacyPlaintext = true)
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

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.nome AS grupoempresarialnome
FROM clientes c
LEFT JOIN gruposempresariais g ON g.id = c.grupoempresarialid
WHERE c.documentohash = @documentoHash AND c.ativo = TRUE
LIMIT 1;
";
        cmd.Parameters.AddWithValue("@documentoHash", documentoHash);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Cliente? GetByCodigoCliente(string? codigoCliente)
    {
        if (string.IsNullOrWhiteSpace(codigoCliente))
            return null;

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.nome AS grupoempresarialnome
FROM clientes c
LEFT JOIN gruposempresariais g ON g.id = c.grupoempresarialid
WHERE UPPER(c.codigocliente) = UPPER(@codigoCliente) AND c.ativo = TRUE
LIMIT 1;
";
        cmd.Parameters.AddWithValue("@codigoCliente", codigoCliente.Trim());

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Cliente? GetById(int id)
    {
        if (id <= 0)
            return null;

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.nome AS grupoempresarialnome
FROM clientes c
LEFT JOIN gruposempresariais g ON g.id = c.grupoempresarialid
WHERE c.id = @id AND c.ativo = TRUE
LIMIT 1;
";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Cliente> ListarTodos()
    {
        var list = new List<Cliente>();

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.nome AS grupoempresarialnome
FROM clientes c
LEFT JOIN gruposempresariais g ON g.id = c.grupoempresarialid
WHERE c.ativo = TRUE
ORDER BY c.nome ASC;
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

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.*, g.nome AS grupoempresarialnome
FROM clientes c
LEFT JOIN gruposempresariais g ON g.id = c.grupoempresarialid
WHERE c.ativo = TRUE
  AND (
    @termoVazio = TRUE
    OR c.nome ILIKE @termoNome
    OR c.nomefantasia ILIKE @termoNome
    OR c.codigocliente ILIKE @termoCodigo
    OR c.documentohash = @termoDocumentoHash
    OR g.nome ILIKE @termoNome
  )
ORDER BY
  CASE
    WHEN @termoVazio = TRUE THEN 100
    WHEN c.codigocliente = @termoCodigoExato THEN 0
    WHEN c.documentohash = @termoDocumentoHash THEN 1
    WHEN c.nome ILIKE @termoExato THEN 2
    WHEN c.nomefantasia ILIKE @termoExato THEN 3
    WHEN g.nome ILIKE @termoExato THEN 4
    WHEN c.codigocliente ILIKE @termoCodigoPrefixo THEN 5
    WHEN c.nome ILIKE @termoPrefixo THEN 6
    WHEN c.nomefantasia ILIKE @termoPrefixo THEN 7
    WHEN g.nome ILIKE @termoPrefixo THEN 8
    ELSE 20
  END,
  c.nome ASC,
  c.id ASC
LIMIT @limit OFFSET @offset;
";
        cmd.Parameters.AddWithValue("@termoVazio", termoNormalizado is null);
        cmd.Parameters.AddWithValue("@termoNome", (object?)termoNome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoPrefixo", (object?)termoPrefixo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoCodigo", (object?)termoCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoCodigoExato", (object?)termoUpper ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoCodigoPrefixo", (object?)termoCodigoPrefixo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoDocumentoHash", (object?)termoDocumentoHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoExato", (object?)termoNormalizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", tamanhoNormalizado);
        cmd.Parameters.AddWithValue("@offset", offset);

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

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(1)
FROM clientes c
LEFT JOIN gruposempresariais g ON g.id = c.grupoempresarialid
WHERE c.ativo = TRUE
  AND (
    @termoVazio = TRUE
    OR c.nome ILIKE @termoNome
    OR c.nomefantasia ILIKE @termoNome
    OR c.codigocliente ILIKE @termoCodigo
    OR c.documentohash = @termoDocumentoHash
    OR g.nome ILIKE @termoNome
  );
";
        cmd.Parameters.AddWithValue("@termoVazio", termoNormalizado is null);
        cmd.Parameters.AddWithValue("@termoNome", (object?)termoNome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoCodigo", (object?)termoCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@termoDocumentoHash", (object?)termoDocumentoHash ?? DBNull.Value);

        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
            return 0;

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public int Create(Cliente cliente)
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO clientes (
  codigocliente, nome, nomefantasia, tipodocumento, documentohash, documentocipher, grupoempresarialid,
  emailcipher, telefonecipher, protecaoversao, ativo, criadoporuserid, criadoemutc, atualizadoemutc
)
VALUES (
  @codigoCliente, @nome, @nomeFantasia, @tipoDocumento, @documentoHash, @documentoCipher, @grupoEmpresarialId,
  @emailCipher, @telefoneCipher, @protecaoVersao, @ativo, @criadoPor, @criadoEmUtc, @atualizadoEmUtc
)
RETURNING id;
";
        BindCliente(cmd, cliente);

        object? result;
        try
        {
            result = cmd.ExecuteScalar();
        }
        catch (PostgresException ex) when (EhErroDuplicidadeCodigoCliente(ex))
        {
            throw new CodigoClienteDuplicadoException("Código de cliente duplicado.", ex);
        }
        catch (PostgresException ex) when (EhErroDuplicidadeDocumentoCliente(ex))
        {
            throw new DocumentoClienteDuplicadoException("Documento de cliente duplicado.", ex);
        }
        catch (PostgresException ex) when (EhErroUnicidade(ex))
        {
            throw new DocumentoClienteDuplicadoException("Documento ou código de cliente duplicado.", ex);
        }

        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException("Create cliente failed: no identity returned.");

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public void Update(Cliente cliente)
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE clientes SET
  codigocliente = @codigoCliente,
  nome = @nome,
  nomefantasia = @nomeFantasia,
  tipodocumento = @tipoDocumento,
  documentohash = @documentoHash,
  documentocipher = @documentoCipher,
  grupoempresarialid = @grupoEmpresarialId,
  emailcipher = @emailCipher,
  telefonecipher = @telefoneCipher,
  protecaoversao = @protecaoVersao,
  ativo = @ativo,
  atualizadoemutc = @atualizadoEmUtc
WHERE id = @id;
";
        BindCliente(cmd, cliente);
        cmd.Parameters.AddWithValue("@id", cliente.Id);
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (EhErroDuplicidadeCodigoCliente(ex))
        {
            throw new CodigoClienteDuplicadoException("Código de cliente duplicado.", ex);
        }
        catch (PostgresException ex) when (EhErroDuplicidadeDocumentoCliente(ex))
        {
            throw new DocumentoClienteDuplicadoException("Documento de cliente duplicado.", ex);
        }
    }

    public void Inativar(int clienteId, int atualizadoPorUserId, DateTime atualizadoEmUtc)
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE clientes
SET ativo = FALSE,
    atualizadoemutc = @atualizadoEmUtc
WHERE id = @id;
";
        cmd.Parameters.AddWithValue("@id", clienteId);
        cmd.Parameters.AddWithValue("@atualizadoEmUtc", atualizadoEmUtc.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public GrupoEmpresarial? GetGrupoById(int grupoId)
    {
        if (grupoId <= 0)
            return null;

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM gruposempresariais WHERE id = @id AND ativo = TRUE LIMIT 1";
        cmd.Parameters.AddWithValue("@id", grupoId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapGrupo(reader) : null;
    }

    public GrupoEmpresarial? GetGrupoByNomeNormalizado(string? nomeNormalizado)
    {
        if (string.IsNullOrWhiteSpace(nomeNormalizado))
            return null;

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM gruposempresariais WHERE nomenormalizado = @nomeNormalizado AND ativo = TRUE LIMIT 1";
        cmd.Parameters.AddWithValue("@nomeNormalizado", nomeNormalizado.Trim().ToUpperInvariant());

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapGrupo(reader) : null;
    }

    public IReadOnlyList<GrupoEmpresarial> ListarGrupos()
    {
        var list = new List<GrupoEmpresarial>();

        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM gruposempresariais
WHERE ativo = TRUE
ORDER BY nome ASC;
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
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO gruposempresariais (nome, nomenormalizado, ativo, criadoporuserid, criadoemutc, atualizadoemutc)
VALUES (@nome, @nomeNormalizado, @ativo, @criadoPor, @criadoEmUtc, @atualizadoEmUtc)
RETURNING id;
";
        cmd.Parameters.AddWithValue("@nome", grupo.Nome);
        cmd.Parameters.AddWithValue("@nomeNormalizado", grupo.NomeNormalizado);
        cmd.Parameters.AddWithValue("@ativo", grupo.Ativo);
        cmd.Parameters.AddWithValue("@criadoPor", grupo.CriadoPorUserId);
        cmd.Parameters.AddWithValue("@criadoEmUtc", grupo.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@atualizadoEmUtc", grupo.AtualizadoEmUtc.ToString("o"));

        object? result;
        try
        {
            result = cmd.ExecuteScalar();
        }
        catch (PostgresException ex) when (EhErroUnicidade(ex))
        {
            throw new DocumentoClienteDuplicadoException("Grupo empresarial duplicado.", ex);
        }

        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException("Create grupo failed: no identity returned.");

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private void BindCliente(NpgsqlCommand cmd, Cliente cliente)
    {
        var documentoDigits = SomenteDigitos(cliente.Documento);
        if (string.IsNullOrWhiteSpace(documentoDigits))
            throw new InvalidOperationException("Documento do cliente inválido para persistência.");

        cmd.Parameters.AddWithValue("@codigoCliente", cliente.CodigoCliente);
        cmd.Parameters.AddWithValue("@nome", cliente.Nome);
        cmd.Parameters.AddWithValue("@nomeFantasia", (object?)cliente.NomeFantasia ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tipoDocumento", cliente.TipoDocumento.ToString());
        cmd.Parameters.AddWithValue("@documentoHash", ComputeDocumentoHash(documentoDigits));
        cmd.Parameters.AddWithValue("@documentoCipher", EncryptSensitive(documentoDigits));
        cmd.Parameters.AddWithValue("@grupoEmpresarialId", (object?)cliente.GrupoEmpresarialId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@emailCipher", (object?)EncryptSensitiveNullable(cliente.Email) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telefoneCipher", (object?)EncryptSensitiveNullable(cliente.Telefone) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@protecaoVersao", 1);
        cmd.Parameters.AddWithValue("@ativo", cliente.Ativo);
        cmd.Parameters.AddWithValue("@criadoPor", cliente.CriadoPorUserId);
        cmd.Parameters.AddWithValue("@criadoEmUtc", cliente.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@atualizadoEmUtc", cliente.AtualizadoEmUtc.ToString("o"));
    }

    private Cliente Map(NpgsqlDataReader reader)
    {
        return new Cliente
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            CodigoCliente = reader.GetString(reader.GetOrdinal("codigocliente")),
            Nome = reader.GetString(reader.GetOrdinal("nome")),
            NomeFantasia = ReadNullableString(reader, "nomefantasia"),
            TipoDocumento = Enum.TryParse<TipoDocumentoCliente>(reader.GetString(reader.GetOrdinal("tipodocumento")), true, out var tipo)
                ? tipo
                : TipoDocumentoCliente.CNPJ,
            Documento = DecryptSensitive(reader.GetString(reader.GetOrdinal("documentocipher")), "documento"),
            GrupoEmpresarialId = ReadNullableInt(reader, "grupoempresarialid"),
            GrupoEmpresarialNome = ReadNullableString(reader, "grupoempresarialnome"),
            Email = DecryptSensitiveNullable(ReadNullableString(reader, "emailcipher"), "email"),
            Telefone = DecryptSensitiveNullable(ReadNullableString(reader, "telefonecipher"), "telefone"),
            Ativo = reader.GetBoolean(reader.GetOrdinal("ativo")),
            CriadoPorUserId = reader.GetInt32(reader.GetOrdinal("criadoporuserid")),
            CriadoEmUtc = ReadRequiredDate(reader, "criadoemutc"),
            AtualizadoEmUtc = ReadRequiredDate(reader, "atualizadoemutc")
        };
    }

    private static GrupoEmpresarial MapGrupo(NpgsqlDataReader reader)
    {
        return new GrupoEmpresarial
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Nome = reader.GetString(reader.GetOrdinal("nome")),
            NomeNormalizado = reader.GetString(reader.GetOrdinal("nomenormalizado")),
            Ativo = reader.GetBoolean(reader.GetOrdinal("ativo")),
            CriadoPorUserId = reader.GetInt32(reader.GetOrdinal("criadoporuserid")),
            CriadoEmUtc = ReadRequiredDate(reader, "criadoemutc"),
            AtualizadoEmUtc = ReadRequiredDate(reader, "atualizadoemutc")
        };
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime ReadRequiredDate(NpgsqlDataReader reader, string column)
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

    private string DecryptSensitive(string cipherText, string legacyColumn)
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

        throw new InvalidOperationException($"Proteção de dados obrigatória sem chave BYOK válida para coluna {legacyColumn}.");
    }

    private string? DecryptSensitiveNullable(string? cipherText, string legacyColumn)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return null;

        return DecryptSensitive(cipherText, legacyColumn);
    }

    private static string SomenteDigitos(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static bool EhErroUnicidade(PostgresException ex)
    {
        return ex.SqlState == "23505";
    }

    private static bool EhErroDuplicidadeCodigoCliente(PostgresException ex)
    {
        if (!EhErroUnicidade(ex))
            return false;

        var constraint = ex.ConstraintName ?? string.Empty;
        return constraint.Contains("ux_clientes_codigocliente", StringComparison.OrdinalIgnoreCase) ||
               constraint.Contains("clientes_codigocliente_key", StringComparison.OrdinalIgnoreCase) ||
               ex.MessageText.Contains("codigocliente", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EhErroDuplicidadeDocumentoCliente(PostgresException ex)
    {
        if (!EhErroUnicidade(ex))
            return false;

        var constraint = ex.ConstraintName ?? string.Empty;
        return constraint.Contains("ux_clientes_documentohash_ativo", StringComparison.OrdinalIgnoreCase) ||
               constraint.Contains("clientes_documentohash_key", StringComparison.OrdinalIgnoreCase) ||
               ex.MessageText.Contains("documentohash", StringComparison.OrdinalIgnoreCase);
    }
}
