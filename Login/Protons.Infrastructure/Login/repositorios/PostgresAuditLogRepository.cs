using System;
using System.Globalization;
using Npgsql;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Login.Repositories;

public sealed class PostgresAuditLogRepository : IAuditLogRepository
{
    private const int MaxHashChainRetries = 5;
    private readonly PostgresDb _db;

    public PostgresAuditLogRepository(PostgresDb db)
    {
        _db = db;
    }

    public void Insert(AuditLogEntry entry)
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO AuditLog (TimestampUtc, UserId, EmailSnapshot, Acao, Resultado, Detalhes, Maquina, VersaoApp, PrevHash, Hash)
VALUES (@ts, @userId, @email, @acao, @resultado, @detalhes, @maquina, @versao, @prev, @hash);
";
        Bind(cmd, entry);
        cmd.ExecuteNonQuery();
    }

    public string? GetLastHash()
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastHash FROM AuditChain WHERE Id = 1";
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    public void InsertWithHashChain(AuditLogEntry entry, Func<AuditLogEntry, string?, string> computeHash)
    {
        for (var attempt = 0; attempt < MaxHashChainRetries; attempt++)
        {
            using var connection = new NpgsqlConnection(_db.ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            var prev = GetChainHash(connection, transaction);
            entry.PrevHash = prev;
            entry.Hash = computeHash(entry, prev);

            using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            if (entry.Hash is null)
                throw new InvalidOperationException("Hash da auditoria não pode ser nulo quando hash chain está ativo.");

            if (prev is null)
            {
                updateCmd.CommandText = "UPDATE AuditChain SET LastHash = @hash WHERE Id = 1 AND LastHash IS NULL";
                updateCmd.Parameters.AddWithValue("@hash", entry.Hash);
            }
            else
            {
                updateCmd.CommandText = "UPDATE AuditChain SET LastHash = @hash WHERE Id = 1 AND LastHash = @prev";
                updateCmd.Parameters.AddWithValue("@hash", entry.Hash);
                updateCmd.Parameters.AddWithValue("@prev", prev);
            }

            var updated = updateCmd.ExecuteNonQuery();
            if (updated == 0)
            {
                transaction.Rollback();
                continue;
            }

            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
INSERT INTO AuditLog (TimestampUtc, UserId, EmailSnapshot, Acao, Resultado, Detalhes, Maquina, VersaoApp, PrevHash, Hash)
VALUES (@ts, @userId, @email, @acao, @resultado, @detalhes, @maquina, @versao, @prev, @hash);
";
            Bind(insertCmd, entry);
            insertCmd.ExecuteNonQuery();

            transaction.Commit();
            return;
        }

        throw new InvalidOperationException("Hash chain update failed after multiple retries.");
    }

    public IReadOnlyList<AuditLogEntry> GetPage(int page, int pageSize)
    {
        if (page < 1)
            page = 1;
        if (pageSize <= 0)
            pageSize = 50;

        var list = new List<AuditLogEntry>();
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AuditLog ORDER BY Id DESC LIMIT @take OFFSET @skip";
        cmd.Parameters.AddWithValue("@take", pageSize);
        cmd.Parameters.AddWithValue("@skip", (page - 1) * pageSize);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    private static string? GetChainHash(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT LastHash FROM AuditChain WHERE Id = 1";
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    private static void Bind(NpgsqlCommand cmd, AuditLogEntry entry)
    {
        cmd.Parameters.AddWithValue("@ts", entry.TimestampUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@userId", (object?)entry.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)entry.EmailSnapshot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@acao", entry.Acao);
        cmd.Parameters.AddWithValue("@resultado", entry.Resultado);
        cmd.Parameters.AddWithValue("@detalhes", (object?)entry.Detalhes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@maquina", entry.Maquina);
        cmd.Parameters.AddWithValue("@versao", entry.VersaoApp);
        cmd.Parameters.AddWithValue("@prev", (object?)entry.PrevHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hash", (object?)entry.Hash ?? DBNull.Value);
    }

    // PostgreSQL retorna nomes de colunas em minúsculas (identificadores não citados).
    private static AuditLogEntry Map(NpgsqlDataReader reader)
    {
        var tsOrdinal = reader.GetOrdinal("timestamputc");
        var userIdOrdinal = reader.GetOrdinal("userid");
        var emailOrdinal = reader.GetOrdinal("emailsnapshot");
        var detalhesOrdinal = reader.GetOrdinal("detalhes");
        var prevOrdinal = reader.GetOrdinal("prevhash");
        var hashOrdinal = reader.GetOrdinal("hash");

        return new AuditLogEntry
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            TimestampUtc = TryReadTimestamp(reader, tsOrdinal),
            UserId = reader.IsDBNull(userIdOrdinal) ? null : reader.GetInt32(userIdOrdinal),
            EmailSnapshot = reader.IsDBNull(emailOrdinal) ? null : reader.GetString(emailOrdinal),
            Acao = reader.GetString(reader.GetOrdinal("acao")),
            Resultado = reader.GetString(reader.GetOrdinal("resultado")),
            Detalhes = reader.IsDBNull(detalhesOrdinal) ? null : reader.GetString(detalhesOrdinal),
            Maquina = reader.GetString(reader.GetOrdinal("maquina")),
            VersaoApp = reader.GetString(reader.GetOrdinal("versaoapp")),
            PrevHash = reader.IsDBNull(prevOrdinal) ? null : reader.GetString(prevOrdinal),
            Hash = reader.IsDBNull(hashOrdinal) ? null : reader.GetString(hashOrdinal)
        };
    }

    private static DateTime TryReadTimestamp(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return DateTime.UtcNow;
        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
    }
}
