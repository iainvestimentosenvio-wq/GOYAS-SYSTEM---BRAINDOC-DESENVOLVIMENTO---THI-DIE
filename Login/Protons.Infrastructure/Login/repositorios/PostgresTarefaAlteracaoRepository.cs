using Npgsql;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Login.Repositories;

public sealed class PostgresTarefaAlteracaoRepository : ITarefaAlteracaoRepository
{
    private readonly PostgresDb _db;

    public PostgresTarefaAlteracaoRepository(PostgresDb db)
    {
        _db = db;
    }

    public void RegistrarAlteracao(TarefaAlteracao alteracao)
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO tarefaalteracoes
  (tarefaid, alteradoporuserid, alteradopornome, campoalterado, valoranterior, valornovo, alteradoemutc)
VALUES
  (@tarefaId, @userId, @nome, @campo, @anterior, @novo, @em);";
        cmd.Parameters.AddWithValue("@tarefaId", alteracao.TarefaId);
        cmd.Parameters.AddWithValue("@userId", alteracao.AlteradoPorUserId);
        cmd.Parameters.AddWithValue("@nome", alteracao.AlteradoPorNome);
        cmd.Parameters.AddWithValue("@campo", alteracao.CampoAlterado);
        cmd.Parameters.AddWithValue("@anterior", (object?)alteracao.ValorAnterior ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@novo", (object?)alteracao.ValorNovo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@em", alteracao.AlteradoEmUtc);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TarefaAlteracao> ListarPorTarefa(int tarefaId, int limit = 50)
    {
        var list = new List<TarefaAlteracao>();
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, tarefaid, alteradoporuserid, alteradopornome, campoalterado, valoranterior, valornovo, alteradoemutc
FROM tarefaalteracoes
WHERE tarefaid = @tarefaId
ORDER BY alteradoemutc DESC
LIMIT @limit;";
        cmd.Parameters.AddWithValue("@tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TarefaAlteracao
            {
                Id = reader.GetInt32(0),
                TarefaId = reader.GetInt32(1),
                AlteradoPorUserId = reader.GetInt32(2),
                AlteradoPorNome = reader.GetString(3),
                CampoAlterado = reader.GetString(4),
                ValorAnterior = reader.IsDBNull(5) ? null : reader.GetString(5),
                ValorNovo = reader.IsDBNull(6) ? null : reader.GetString(6),
                AlteradoEmUtc = reader.GetDateTime(7).ToUniversalTime()
            });
        }
        return list;
    }
}
