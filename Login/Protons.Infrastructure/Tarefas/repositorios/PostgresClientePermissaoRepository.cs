using System.Globalization;
using Npgsql;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class PostgresClientePermissaoRepository : IClientePermissaoRepository
{
    private readonly PostgresDb _db;

    public PostgresClientePermissaoRepository(PostgresDb db)
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
WHERE ClienteId = @clienteId
ORDER BY UserId ASC;
";
        cmd.Parameters.AddWithValue("@clienteId", clienteId);

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
WHERE ClienteId = @clienteId AND UserId = @userId
LIMIT 1;
";
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        cmd.Parameters.AddWithValue("@userId", userId);

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
            limpar.CommandText = "DELETE FROM ClientePermissoesUsuarios WHERE ClienteId = @clienteId";
            limpar.Parameters.AddWithValue("@clienteId", clienteId);
            limpar.ExecuteNonQuery();
            tx.Commit();
            return;
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM ClientePermissoesUsuarios WHERE ClienteId = @clienteId AND UserId <> ALL(@ids)";
            delete.Parameters.AddWithValue("@clienteId", clienteId);
            delete.Parameters.AddWithValue("@ids", entradas.Select(x => x.UserId).ToArray());
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
  @clienteId, @userId, @podeEditar, @concedidoPorUserId, @criadoEmUtc, @atualizadoEmUtc
)
ON CONFLICT (ClienteId, UserId) DO UPDATE SET
  PodeEditar = EXCLUDED.PodeEditar,
  ConcedidoPorUserId = EXCLUDED.ConcedidoPorUserId,
  AtualizadoEmUtc = EXCLUDED.AtualizadoEmUtc;
";
            upsert.Parameters.AddWithValue("@clienteId", clienteId);
            upsert.Parameters.AddWithValue("@userId", entrada.UserId);
            upsert.Parameters.AddWithValue("@podeEditar", entrada.PodeEditar);
            upsert.Parameters.AddWithValue("@concedidoPorUserId", concedidoPorUserId);
            upsert.Parameters.AddWithValue("@criadoEmUtc", nowUtc.ToString("o"));
            upsert.Parameters.AddWithValue("@atualizadoEmUtc", nowUtc.ToString("o"));
            upsert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static ClientePermissaoUsuario Map(NpgsqlDataReader reader)
    {
        return new ClientePermissaoUsuario
        {
            ClienteId = reader.GetInt32(reader.GetOrdinal("clienteid")),
            UserId = reader.GetInt32(reader.GetOrdinal("userid")),
            PodeEditar = reader.GetBoolean(reader.GetOrdinal("podeeditar")),
            ConcedidoPorUserId = reader.GetInt32(reader.GetOrdinal("concedidoporuserid")),
            CriadoEmUtc = ReadDate(reader, "criadoemutc"),
            AtualizadoEmUtc = ReadDate(reader, "atualizadoemutc")
        };
    }

    private static DateTime ReadDate(NpgsqlDataReader reader, string column)
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
