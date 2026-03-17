using System.Globalization;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteEsteiraRepository : IEsteiraRepository
{
    private readonly SqliteDb _db;

    public SqliteEsteiraRepository(SqliteDb db)
    {
        _db = db;
    }

    public Esteira? GetById(int id)
    {
        if (id <= 0)
            return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Esteiras WHERE Id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Esteira> ListarPorCliente(int clienteId)
    {
        var list = new List<Esteira>();

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM Esteiras
WHERE ClienteId = $clienteId AND Ativa = 1
ORDER BY Ordem ASC, Id ASC;
";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public int Create(Esteira esteira)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Esteiras (ClienteId, Nome, Ordem, Ativa, CriadoEmUtc)
VALUES ($clienteId, $nome, $ordem, $ativa, $criadoEmUtc);
SELECT last_insert_rowid();
";
        cmd.Parameters.AddWithValue("$clienteId", esteira.ClienteId);
        cmd.Parameters.AddWithValue("$nome", esteira.Nome);
        cmd.Parameters.AddWithValue("$ordem", esteira.Ordem);
        cmd.Parameters.AddWithValue("$ativa", esteira.Ativa ? 1 : 0);
        cmd.Parameters.AddWithValue("$criadoEmUtc", esteira.CriadoEmUtc.ToString("o", CultureInfo.InvariantCulture));

        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public void Update(Esteira esteira)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Esteiras SET
  ClienteId = $clienteId,
  Nome = $nome,
  Ordem = $ordem,
  Ativa = $ativa
WHERE Id = $id;
";
        cmd.Parameters.AddWithValue("$id", esteira.Id);
        cmd.Parameters.AddWithValue("$clienteId", esteira.ClienteId);
        cmd.Parameters.AddWithValue("$nome", esteira.Nome);
        cmd.Parameters.AddWithValue("$ordem", esteira.Ordem);
        cmd.Parameters.AddWithValue("$ativa", esteira.Ativa ? 1 : 0);

        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Esteiras SET Ativa = 0 WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static Esteira Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("Id")),
        ClienteId = reader.GetInt32(reader.GetOrdinal("ClienteId")),
        Nome = reader.GetString(reader.GetOrdinal("Nome")),
        Ordem = reader.GetInt32(reader.GetOrdinal("Ordem")),
        Ativa = reader.GetInt32(reader.GetOrdinal("Ativa")) == 1,
        CriadoEmUtc = DateTime.Parse(
            reader.GetString(reader.GetOrdinal("CriadoEmUtc")),
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
