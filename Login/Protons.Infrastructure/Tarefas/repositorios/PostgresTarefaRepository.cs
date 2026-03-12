using System.Globalization;
using System.Linq;
using Npgsql;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class PostgresTarefaRepository : ITarefaRepository
{
    private readonly PostgresDb _db;

    public PostgresTarefaRepository(PostgresDb db)
    {
        _db = db;
    }

    public Tarefa? GetById(int id)
    {
        if (id <= 0)
            return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Tarefas WHERE Id = @id AND Ativa = TRUE LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Tarefa> Buscar(
        int clienteId,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        int? responsavelUserId)
    {
        var termoNormalizado = string.IsNullOrWhiteSpace(termo) ? null : $"%{termo.Trim()}%";
        var list = new List<Tarefa>();

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM Tarefas
WHERE ClienteId = @clienteId
  AND Ativa = TRUE
  AND (@termo IS NULL OR Titulo ILIKE @termo)
  AND (@status IS NULL OR Status = @status)
  AND (@inicio IS NULL OR VencimentoUtc >= @inicio)
  AND (@fim IS NULL OR VencimentoUtc <= @fim)
  AND (@responsavelId IS NULL OR ResponsavelUserId = @responsavelId)
ORDER BY VencimentoUtc ASC, Id DESC;
";
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        cmd.Parameters.AddWithValue("@termo", (object?)termoNormalizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)status?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inicio", (object?)vencimentoInicioUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fim", (object?)vencimentoFimUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@responsavelId", (object?)responsavelUserId ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public IReadOnlyList<Tarefa> BuscarPorClienteIds(
        IReadOnlyList<int> clienteIds,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        int? responsavelUserId)
    {
        if (clienteIds is null || clienteIds.Count == 0)
            return [];

        var termoNormalizado = string.IsNullOrWhiteSpace(termo) ? null : $"%{termo.Trim()}%";
        var list = new List<Tarefa>();

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM Tarefas
WHERE ClienteId = ANY(@clienteIds)
  AND Ativa = TRUE
  AND (@termo IS NULL OR Titulo ILIKE @termo)
  AND (@status IS NULL OR Status = @status)
  AND (@inicio IS NULL OR VencimentoUtc >= @inicio)
  AND (@fim IS NULL OR VencimentoUtc <= @fim)
  AND (@responsavelId IS NULL OR ResponsavelUserId = @responsavelId)
ORDER BY VencimentoUtc ASC, Id DESC;
";
        cmd.Parameters.AddWithValue("@clienteIds", clienteIds.ToArray());
        cmd.Parameters.AddWithValue("@termo", (object?)termoNormalizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)status?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inicio", (object?)vencimentoInicioUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fim", (object?)vencimentoFimUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@responsavelId", (object?)responsavelUserId ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public int Create(Tarefa tarefa)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Tarefas (
  ClienteId, FerramentaId, Titulo, VencimentoUtc, ResponsavelUserId, Status, Recorrencia, DiaRecorrenciaMensal,
  Ativa, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc, ConcluidaEmUtc
)
VALUES (
  @clienteId, @ferramentaId, @titulo, @vencimentoUtc, @responsavelUserId, @status, @recorrencia, @diaRecorrenciaMensal,
  @ativa, @criadoPorUserId, @criadoEmUtc, @atualizadoEmUtc, @concluidaEmUtc
)
RETURNING Id;
";
        Bind(cmd, tarefa);
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException("Create tarefa failed: no identity returned.");

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public void Update(Tarefa tarefa)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Tarefas SET
  ClienteId = @clienteId,
  FerramentaId = @ferramentaId,
  Titulo = @titulo,
  VencimentoUtc = @vencimentoUtc,
  ResponsavelUserId = @responsavelUserId,
  Status = @status,
  Recorrencia = @recorrencia,
  DiaRecorrenciaMensal = @diaRecorrenciaMensal,
  Ativa = @ativa,
  CriadoPorUserId = @criadoPorUserId,
  CriadoEmUtc = @criadoEmUtc,
  AtualizadoEmUtc = @atualizadoEmUtc,
  ConcluidaEmUtc = @concluidaEmUtc
WHERE Id = @id;
";
        Bind(cmd, tarefa);
        cmd.Parameters.AddWithValue("@id", tarefa.Id);
        cmd.ExecuteNonQuery();
    }

    public bool TryMarkAsInProgress(int tarefaId, DateTime atualizadoEmUtc)
    {
        if (tarefaId <= 0)
            return false;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Tarefas
SET Status = @statusNovo,
    AtualizadoEmUtc = @atualizadoEmUtc
WHERE Id = @id
  AND Ativa = TRUE
  AND Status = @statusEsperado;
";
        cmd.Parameters.AddWithValue("@statusNovo", TarefaStatus.EmAndamento.ToString());
        cmd.Parameters.AddWithValue("@atualizadoEmUtc", atualizadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@id", tarefaId);
        cmd.Parameters.AddWithValue("@statusEsperado", TarefaStatus.Agendada.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void Bind(NpgsqlCommand cmd, Tarefa tarefa)
    {
        cmd.Parameters.AddWithValue("@clienteId", tarefa.ClienteId);
        cmd.Parameters.AddWithValue("@ferramentaId", FerramentaTarefaIds.Canonicalizar(tarefa.FerramentaId));
        cmd.Parameters.AddWithValue("@titulo", tarefa.Titulo);
        cmd.Parameters.AddWithValue("@vencimentoUtc", tarefa.VencimentoUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@responsavelUserId", tarefa.ResponsavelUserId);
        cmd.Parameters.AddWithValue("@status", tarefa.Status.ToString());
        cmd.Parameters.AddWithValue("@recorrencia", tarefa.Recorrencia.ToString());
        cmd.Parameters.AddWithValue("@diaRecorrenciaMensal", (object?)tarefa.DiaRecorrenciaMensal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ativa", tarefa.Ativa);
        cmd.Parameters.AddWithValue("@criadoPorUserId", tarefa.CriadoPorUserId);
        cmd.Parameters.AddWithValue("@criadoEmUtc", tarefa.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@atualizadoEmUtc", tarefa.AtualizadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@concluidaEmUtc", (object?)tarefa.ConcluidaEmUtc?.ToString("o") ?? DBNull.Value);
    }

    private static Tarefa Map(NpgsqlDataReader reader)
    {
        return new Tarefa
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            ClienteId = reader.GetInt32(reader.GetOrdinal("clienteid")),
            FerramentaId = ReadString(reader, "ferramentaid", FerramentaTarefaIds.Generica),
            Titulo = reader.GetString(reader.GetOrdinal("titulo")),
            VencimentoUtc = ReadDate(reader, "vencimentoutc"),
            ResponsavelUserId = reader.GetInt32(reader.GetOrdinal("responsaveluserid")),
            Status = Enum.TryParse<TarefaStatus>(reader.GetString(reader.GetOrdinal("status")), true, out var status)
                ? status
                : TarefaStatus.Agendada,
            Recorrencia = Enum.TryParse<TarefaRecorrencia>(reader.GetString(reader.GetOrdinal("recorrencia")), true, out var recorrencia)
                ? recorrencia
                : TarefaRecorrencia.Nenhuma,
            DiaRecorrenciaMensal = ReadNullableInt(reader, "diarecorrenciamensal"),
            Ativa = reader.GetBoolean(reader.GetOrdinal("ativa")),
            CriadoPorUserId = reader.GetInt32(reader.GetOrdinal("criadoporuserid")),
            CriadoEmUtc = ReadDate(reader, "criadoemutc"),
            AtualizadoEmUtc = ReadDate(reader, "atualizadoemutc"),
            ConcluidaEmUtc = ReadNullableDate(reader, "concluidaemutc")
        };
    }

    private static int? ReadNullableInt(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
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

    private static DateTime? ReadNullableDate(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : null;
    }

    private static string ReadString(NpgsqlDataReader reader, string column, string fallback)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
                return fallback;

            var raw = reader.GetString(ordinal);
            return string.IsNullOrWhiteSpace(raw)
                ? fallback
                : FerramentaTarefaIds.Canonicalizar(raw);
        }
        catch (IndexOutOfRangeException)
        {
            return fallback;
        }
    }
}
