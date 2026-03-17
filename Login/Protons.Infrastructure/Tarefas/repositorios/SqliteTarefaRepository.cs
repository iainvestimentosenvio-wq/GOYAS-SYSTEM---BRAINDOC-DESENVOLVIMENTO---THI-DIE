using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteTarefaRepository : ITarefaRepository
{
    private const int MaxClienteIdsPorConsulta = 900;

    private readonly SqliteDb _db;

    public SqliteTarefaRepository(SqliteDb db)
    {
        _db = db;
    }

    public Tarefa? GetById(int id)
    {
        if (id <= 0)
            return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Tarefas WHERE Id = $id AND Ativa = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
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
WHERE ClienteId = $clienteId
  AND Ativa = 1
  AND ($termo IS NULL OR Titulo LIKE $termo COLLATE NOCASE)
  AND ($status IS NULL OR Status = $status)
  AND ($inicio IS NULL OR VencimentoUtc >= $inicio)
  AND ($fim IS NULL OR VencimentoUtc <= $fim)
  AND ($responsavelId IS NULL OR ResponsavelUserId = $responsavelId)
ORDER BY VencimentoUtc ASC, Id DESC
LIMIT 1000;
";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);
        cmd.Parameters.AddWithValue("$termo", (object?)termoNormalizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)status?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inicio", (object?)vencimentoInicioUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fim", (object?)vencimentoFimUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$responsavelId", (object?)responsavelUserId ?? DBNull.Value);

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

        if (clienteIds.Count <= MaxClienteIdsPorConsulta)
        {
            return BuscarPorClienteIdsLote(
                clienteIds,
                termo,
                status,
                vencimentoInicioUtc,
                vencimentoFimUtc,
                responsavelUserId);
        }

        var agregadas = new List<Tarefa>();
        for (var offset = 0; offset < clienteIds.Count; offset += MaxClienteIdsPorConsulta)
        {
            var tamanhoLote = Math.Min(MaxClienteIdsPorConsulta, clienteIds.Count - offset);
            var clienteIdsLote = new int[tamanhoLote];
            for (var i = 0; i < tamanhoLote; i++)
                clienteIdsLote[i] = clienteIds[offset + i];

            agregadas.AddRange(BuscarPorClienteIdsLote(
                clienteIdsLote,
                termo,
                status,
                vencimentoInicioUtc,
                vencimentoFimUtc,
                responsavelUserId));
        }

        return agregadas
            .OrderBy(t => t.VencimentoUtc)
            .ThenByDescending(t => t.Id)
            .ToList();
    }

    private List<Tarefa> BuscarPorClienteIdsLote(
        IReadOnlyList<int> clienteIds,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        int? responsavelUserId)
    {
        var termoNormalizado = string.IsNullOrWhiteSpace(termo) ? null : $"%{termo.Trim()}%";
        var list = new List<Tarefa>();
        var inPlaceholders = string.Join(", ", clienteIds.Select((_, i) => $"$id{i}"));
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
SELECT * FROM Tarefas
WHERE ClienteId IN ({inPlaceholders})
  AND Ativa = 1
  AND ($termo IS NULL OR Titulo LIKE $termo COLLATE NOCASE)
  AND ($status IS NULL OR Status = $status)
  AND ($inicio IS NULL OR VencimentoUtc >= $inicio)
  AND ($fim IS NULL OR VencimentoUtc <= $fim)
  AND ($responsavelId IS NULL OR ResponsavelUserId = $responsavelId)
ORDER BY VencimentoUtc ASC, Id DESC
LIMIT 1000;
";
        for (var i = 0; i < clienteIds.Count; i++)
            cmd.Parameters.AddWithValue($"$id{i}", clienteIds[i]);
        cmd.Parameters.AddWithValue("$termo", (object?)termoNormalizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)status?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inicio", (object?)vencimentoInicioUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fim", (object?)vencimentoFimUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$responsavelId", (object?)responsavelUserId ?? DBNull.Value);

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
  $clienteId, $ferramentaId, $titulo, $vencimentoUtc, $responsavelUserId, $status, $recorrencia, $diaRecorrenciaMensal,
  $ativa, $criadoPorUserId, $criadoEmUtc, $atualizadoEmUtc, $concluidaEmUtc
);
SELECT last_insert_rowid();
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
  ClienteId = $clienteId,
  FerramentaId = $ferramentaId,
  Titulo = $titulo,
  VencimentoUtc = $vencimentoUtc,
  ResponsavelUserId = $responsavelUserId,
  Status = $status,
  Recorrencia = $recorrencia,
  DiaRecorrenciaMensal = $diaRecorrenciaMensal,
  Ativa = $ativa,
  CriadoPorUserId = $criadoPorUserId,
  CriadoEmUtc = $criadoEmUtc,
  AtualizadoEmUtc = $atualizadoEmUtc,
  ConcluidaEmUtc = $concluidaEmUtc
WHERE Id = $id;
";
        Bind(cmd, tarefa);
        cmd.Parameters.AddWithValue("$id", tarefa.Id);
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
SET Status = $statusNovo,
    AtualizadoEmUtc = $atualizadoEmUtc
WHERE Id = $id
  AND Ativa = 1
  AND Status = $statusEsperado;
";
        cmd.Parameters.AddWithValue("$statusNovo", TarefaStatus.EmAndamento.ToString());
        cmd.Parameters.AddWithValue("$atualizadoEmUtc", atualizadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$id", tarefaId);
        cmd.Parameters.AddWithValue("$statusEsperado", TarefaStatus.Agendada.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void Bind(SqliteCommand cmd, Tarefa tarefa)
    {
        cmd.Parameters.AddWithValue("$clienteId", tarefa.ClienteId);
        cmd.Parameters.AddWithValue("$ferramentaId", FerramentaTarefaIds.Canonicalizar(tarefa.FerramentaId));
        cmd.Parameters.AddWithValue("$titulo", tarefa.Titulo);
        cmd.Parameters.AddWithValue("$vencimentoUtc", tarefa.VencimentoUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$responsavelUserId", tarefa.ResponsavelUserId);
        cmd.Parameters.AddWithValue("$status", tarefa.Status.ToString());
        cmd.Parameters.AddWithValue("$recorrencia", tarefa.Recorrencia.ToString());
        cmd.Parameters.AddWithValue("$diaRecorrenciaMensal", (object?)tarefa.DiaRecorrenciaMensal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ativa", tarefa.Ativa ? 1 : 0);
        cmd.Parameters.AddWithValue("$criadoPorUserId", tarefa.CriadoPorUserId);
        cmd.Parameters.AddWithValue("$criadoEmUtc", tarefa.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$atualizadoEmUtc", tarefa.AtualizadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$concluidaEmUtc", (object?)tarefa.ConcluidaEmUtc?.ToString("o") ?? DBNull.Value);
    }

    private static Tarefa Map(SqliteDataReader reader)
    {
        return new Tarefa
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            ClienteId = reader.GetInt32(reader.GetOrdinal("ClienteId")),
            FerramentaId = ReadString(reader, "FerramentaId", FerramentaTarefaIds.Generica),
            Titulo = reader.GetString(reader.GetOrdinal("Titulo")),
            VencimentoUtc = ReadDate(reader, "VencimentoUtc"),
            ResponsavelUserId = reader.GetInt32(reader.GetOrdinal("ResponsavelUserId")),
            Status = Enum.TryParse<TarefaStatus>(reader.GetString(reader.GetOrdinal("Status")), true, out var status)
                ? status
                : TarefaStatus.Agendada,
            Recorrencia = Enum.TryParse<TarefaRecorrencia>(reader.GetString(reader.GetOrdinal("Recorrencia")), true, out var recorrencia)
                ? recorrencia
                : TarefaRecorrencia.Nenhuma,
            DiaRecorrenciaMensal = ReadNullableInt(reader, "DiaRecorrenciaMensal"),
            Ativa = reader.GetInt32(reader.GetOrdinal("Ativa")) == 1,
            CriadoPorUserId = reader.GetInt32(reader.GetOrdinal("CriadoPorUserId")),
            CriadoEmUtc = ReadDate(reader, "CriadoEmUtc"),
            AtualizadoEmUtc = ReadDate(reader, "AtualizadoEmUtc"),
            ConcluidaEmUtc = ReadNullableDate(reader, "ConcluidaEmUtc")
        };
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
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

    private static DateTime? ReadNullableDate(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : null;
    }

    private static string ReadString(SqliteDataReader reader, string column, string fallback)
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
