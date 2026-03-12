using System.Globalization;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteClientePermissaoRepository : IClientePermissaoRepository
{
    private readonly SqliteDb _db;

    public SqliteClientePermissaoRepository(SqliteDb db)
    {
        _db = db;
    }

    public IReadOnlyList<ClientePermissaoUsuario> ListarPorCliente(int clienteId)
    {
        var list = new List<ClientePermissaoUsuario>();
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM ClientePermissoesUsuarios
WHERE ClienteId = $clienteId
ORDER BY UserId ASC;
";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public ClientePermissaoUsuario? Obter(int clienteId, int userId)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM ClientePermissoesUsuarios
WHERE ClienteId = $clienteId AND UserId = $userId
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);
        cmd.Parameters.AddWithValue("$userId", userId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void DefinirPermissoes(int clienteId, int concedidoPorUserId, IReadOnlyList<PermissaoClienteEntrada> permissoes)
    {
        var nowUtc = DateTime.UtcNow;
        var entradas = (permissoes ?? [])
            .Where(p => p.UserId > 0)
            .GroupBy(p => p.UserId)
            .Select(g => new PermissaoClienteEntrada
            {
                UserId = g.Key,
                PodeEditar = g.Any(x => x.PodeEditar)
            })
            .ToList();

        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        if (entradas.Count == 0)
        {
            using var limpar = connection.CreateCommand();
            limpar.Transaction = tx;
            limpar.CommandText = "DELETE FROM ClientePermissoesUsuarios WHERE ClienteId = $clienteId";
            limpar.Parameters.AddWithValue("$clienteId", clienteId);
            limpar.ExecuteNonQuery();
            tx.Commit();
            return;
        }

        var ids = string.Join(",", entradas.Select((_, i) => $"$id{i}"));
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = $"DELETE FROM ClientePermissoesUsuarios WHERE ClienteId = $clienteId AND UserId NOT IN ({ids})";
            delete.Parameters.AddWithValue("$clienteId", clienteId);
            for (var i = 0; i < entradas.Count; i++)
            {
                delete.Parameters.AddWithValue($"$id{i}", entradas[i].UserId);
            }

            delete.ExecuteNonQuery();
        }

        foreach (var entrada in entradas)
        {
            using var upsert = connection.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = @"
INSERT INTO ClientePermissoesUsuarios (
  ClienteId, UserId, PodeEditar, ConcedidoPorUserId, CriadoEmUtc, AtualizadoEmUtc
)
VALUES (
  $clienteId, $userId, $podeEditar, $concedidoPorUserId, $criadoEmUtc, $atualizadoEmUtc
)
ON CONFLICT(ClienteId, UserId) DO UPDATE SET
  PodeEditar = excluded.PodeEditar,
  ConcedidoPorUserId = excluded.ConcedidoPorUserId,
  AtualizadoEmUtc = excluded.AtualizadoEmUtc;
";
            upsert.Parameters.AddWithValue("$clienteId", clienteId);
            upsert.Parameters.AddWithValue("$userId", entrada.UserId);
            upsert.Parameters.AddWithValue("$podeEditar", entrada.PodeEditar ? 1 : 0);
            upsert.Parameters.AddWithValue("$concedidoPorUserId", concedidoPorUserId);
            upsert.Parameters.AddWithValue("$criadoEmUtc", nowUtc.ToString("o"));
            upsert.Parameters.AddWithValue("$atualizadoEmUtc", nowUtc.ToString("o"));
            upsert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static ClientePermissaoUsuario Map(SqliteDataReader reader)
    {
        return new ClientePermissaoUsuario
        {
            ClienteId = reader.GetInt32(reader.GetOrdinal("ClienteId")),
            UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
            PodeEditar = reader.GetInt32(reader.GetOrdinal("PodeEditar")) == 1,
            ConcedidoPorUserId = reader.GetInt32(reader.GetOrdinal("ConcedidoPorUserId")),
            CriadoEmUtc = ReadDate(reader, "CriadoEmUtc"),
            AtualizadoEmUtc = ReadDate(reader, "AtualizadoEmUtc")
        };
    }

    private static DateTime ReadDate(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return DateTime.UtcNow;

        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
    }
}
