using System.Globalization;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteAncorarPdfExecucaoFilaRepository : IAncorarPdfFilaExecucaoRepository
{
    private readonly SqliteDb _db;
    private readonly TimeProvider _timeProvider;

    public SqliteAncorarPdfExecucaoFilaRepository(SqliteDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AncorarPdfFilaEnfileirarResultado Enfileirar(AncorarPdfFilaItem item)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO AncorarPdfExecucaoFila (
    FilaItemId, TarefaId, ClienteId, CicloId, JanelaAlvoUtc,
    PrioridadeExecucao, Status, Motivo,
    EnfileiradoPorUserId, EnfileiradoPorNome, EnfileiradoEmUtc,
    TentativaAtual, TentativasMaximas, CorrelationId, CriadoEmUtc
) VALUES (
    $filaItemId, $tarefaId, $clienteId, $cicloId, $janelaAlvoUtc,
    $prioridadeExecucao, $status, $motivo,
    $enfileiradoPorUserId, $enfileiradoPorNome, $enfileiradoEmUtc,
    $tentativaAtual, $tentativasMaximas, $correlationId, $criadoEmUtc
)
ON CONFLICT DO NOTHING;";

        BindItem(cmd, item);
        var rows = cmd.ExecuteNonQuery();
        return rows > 0
            ? new AncorarPdfFilaEnfileirarResultado(true, item.FilaItemId)
            : new AncorarPdfFilaEnfileirarResultado(false, null, "duplicado");
    }

    public AncorarPdfFilaItem? TentarReclamar(string workerId)
    {
        // SQLite: single-writer + WAL — não suporta FOR UPDATE SKIP LOCKED.
        // Usa SELECT + UPDATE atômico com condição WHERE Status='Aguardando'.
        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        AncorarPdfFilaItem? candidato = null;
        using (var cmdSel = connection.CreateCommand())
        {
            cmdSel.Transaction = tx;
            cmdSel.CommandText = @"
SELECT FilaItemId FROM AncorarPdfExecucaoFila
WHERE Status = 'Aguardando'
ORDER BY PrioridadeExecucao ASC, EnfileiradoEmUtc ASC
LIMIT 1;";
            var id = cmdSel.ExecuteScalar()?.ToString();
            if (id is null)
            {
                tx.Rollback();
                return null;
            }

            using var cmdGet = connection.CreateCommand();
            cmdGet.Transaction = tx;
            cmdGet.CommandText = "SELECT * FROM AncorarPdfExecucaoFila WHERE FilaItemId = $id LIMIT 1;";
            cmdGet.Parameters.AddWithValue("$id", id);
            using var reader = cmdGet.ExecuteReader();
            if (reader.Read())
                candidato = MapItem(reader);
        }

        if (candidato is null)
        {
            tx.Rollback();
            return null;
        }

        using (var cmdUpd = connection.CreateCommand())
        {
            cmdUpd.Transaction = tx;
            cmdUpd.CommandText = @"
UPDATE AncorarPdfExecucaoFila
SET Status = 'EmProcessamento'
WHERE FilaItemId = $id AND Status = 'Aguardando';";
            cmdUpd.Parameters.AddWithValue("$id", candidato.FilaItemId);
            var updated = cmdUpd.ExecuteNonQuery();
            if (updated == 0)
            {
                tx.Rollback();
                return null;
            }
        }

        tx.Commit();
        return candidato with { Status = AncorarPdfFilaStatus.EmProcessamento };
    }

    public void AtualizarStatus(
        string filaItemId,
        AncorarPdfFilaStatus status,
        DateTime? iniciadoEmUtc = null,
        DateTime? finalizadoEmUtc = null,
        AncorarPdfFilaFalhaCategoria? categoriaFalha = null,
        string? erroCodigo = null,
        string? erroDetalhe = null,
        int? tentativaAtual = null)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE AncorarPdfExecucaoFila
SET Status = $status,
    IniciadoEmUtc = COALESCE($iniciadoEmUtc, IniciadoEmUtc),
    FinalizadoEmUtc = COALESCE($finalizadoEmUtc, FinalizadoEmUtc),
    CategoriaFalha = COALESCE($categoriaFalha, CategoriaFalha),
    ErroCodigo = COALESCE($erroCodigo, ErroCodigo),
    ErroDetalhe = COALESCE($erroDetalhe, ErroDetalhe),
    TentativaAtual = COALESCE($tentativaAtual, TentativaAtual)
WHERE FilaItemId = $filaItemId;";

        cmd.Parameters.AddWithValue("$filaItemId", filaItemId);
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$iniciadoEmUtc",
            (object?)iniciadoEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finalizadoEmUtc",
            (object?)finalizadoEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$categoriaFalha",
            (object?)categoriaFalha?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$erroCodigo", (object?)erroCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$erroDetalhe", (object?)erroDetalhe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tentativaAtual", (object?)tentativaAtual ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool TentarCancelar(string filaItemId, string canceladoPorNome, DateTime canceladoEmUtc)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE AncorarPdfExecucaoFila
SET Status = 'Cancelado',
    CanceladoPorNome = $canceladoPorNome,
    CanceladoEmUtc = $canceladoEmUtc,
    FinalizadoEmUtc = $canceladoEmUtc
WHERE FilaItemId = $filaItemId AND Status = 'Aguardando';";

        cmd.Parameters.AddWithValue("$filaItemId", filaItemId);
        cmd.Parameters.AddWithValue("$canceladoPorNome", canceladoPorNome);
        cmd.Parameters.AddWithValue("$canceladoEmUtc",
            canceladoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<AncorarPdfFilaItem> ListarParaReidratar(int limite = 200)
    {
        var lim = Math.Clamp(limite, 1, 1000);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT f.* FROM AncorarPdfExecucaoFila f
LEFT JOIN AncorarPdfExecucaoLease l
    ON l.FilaItemId = f.FilaItemId AND l.Ativa = 1
WHERE f.Status = 'Aguardando' AND l.LeaseId IS NULL
ORDER BY f.PrioridadeExecucao ASC, f.EnfileiradoEmUtc ASC
LIMIT $limite;";
        cmd.Parameters.AddWithValue("$limite", lim);
        var result = new List<AncorarPdfFilaItem>(lim);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapItem(reader));
        return result;
    }

    public IReadOnlyList<AncorarPdfFilaItem> Listar(AncorarPdfFilaFiltro filtro)
    {
        var limite = Math.Clamp(filtro.Limite, 1, 500);
        var offset = Math.Max(0, filtro.Offset);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        var where = new System.Text.StringBuilder("WHERE 1=1");

        if (filtro.ClienteId.HasValue)
        {
            where.Append(" AND ClienteId = $clienteId");
            cmd.Parameters.AddWithValue("$clienteId", filtro.ClienteId.Value);
        }
        if (filtro.TarefaId.HasValue)
        {
            where.Append(" AND TarefaId = $tarefaId");
            cmd.Parameters.AddWithValue("$tarefaId", filtro.TarefaId.Value);
        }
        if (filtro.Status.HasValue)
        {
            where.Append(" AND Status = $status");
            cmd.Parameters.AddWithValue("$status", filtro.Status.Value.ToString());
        }
        if (filtro.DataInicioUtc.HasValue)
        {
            where.Append(" AND EnfileiradoEmUtc >= $dataInicio");
            cmd.Parameters.AddWithValue("$dataInicio",
                filtro.DataInicioUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }
        if (filtro.DataFimUtc.HasValue)
        {
            where.Append(" AND EnfileiradoEmUtc <= $dataFim");
            cmd.Parameters.AddWithValue("$dataFim",
                filtro.DataFimUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        cmd.CommandText = $@"
SELECT * FROM AncorarPdfExecucaoFila
{where}
ORDER BY EnfileiradoEmUtc DESC
LIMIT $limite OFFSET $offset;";
        cmd.Parameters.AddWithValue("$limite", limite);
        cmd.Parameters.AddWithValue("$offset", offset);

        var result = new List<AncorarPdfFilaItem>(limite);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapItem(reader));
        return result;
    }

    public int ContarAguardando()
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AncorarPdfExecucaoFila WHERE Status = 'Aguardando';";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static void BindItem(SqliteCommand cmd, AncorarPdfFilaItem item)
    {
        cmd.Parameters.AddWithValue("$filaItemId", item.FilaItemId);
        cmd.Parameters.AddWithValue("$tarefaId", item.TarefaId);
        cmd.Parameters.AddWithValue("$clienteId", item.ClienteId);
        cmd.Parameters.AddWithValue("$cicloId", item.CicloId);
        cmd.Parameters.AddWithValue("$janelaAlvoUtc",
            item.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$prioridadeExecucao", item.PrioridadeExecucao);
        cmd.Parameters.AddWithValue("$status", item.Status.ToString());
        cmd.Parameters.AddWithValue("$motivo", item.Motivo);
        cmd.Parameters.AddWithValue("$enfileiradoPorUserId", item.EnfileiradoPorUserId);
        cmd.Parameters.AddWithValue("$enfileiradoPorNome", item.EnfileiradoPorNome);
        cmd.Parameters.AddWithValue("$enfileiradoEmUtc",
            item.EnfileiradoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$tentativaAtual", item.TentativaAtual);
        cmd.Parameters.AddWithValue("$tentativasMaximas", item.TentativasMaximas);
        cmd.Parameters.AddWithValue("$correlationId", item.CorrelationId);
        cmd.Parameters.AddWithValue("$criadoEmUtc",
            item.CriadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
    }

    private AncorarPdfFilaItem MapItem(SqliteDataReader r)
    {
        return new AncorarPdfFilaItem
        {
            FilaItemId = r.GetString(r.GetOrdinal("FilaItemId")),
            TarefaId = r.GetInt32(r.GetOrdinal("TarefaId")),
            ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
            CicloId = r.GetString(r.GetOrdinal("CicloId")),
            JanelaAlvoUtc = ReadDate(r, "JanelaAlvoUtc"),
            PrioridadeExecucao = r.GetInt32(r.GetOrdinal("PrioridadeExecucao")),
            Status = Enum.Parse<AncorarPdfFilaStatus>(r.GetString(r.GetOrdinal("Status"))),
            Motivo = r.GetString(r.GetOrdinal("Motivo")),
            EnfileiradoPorUserId = r.GetInt32(r.GetOrdinal("EnfileiradoPorUserId")),
            EnfileiradoPorNome = r.GetString(r.GetOrdinal("EnfileiradoPorNome")),
            EnfileiradoEmUtc = ReadDate(r, "EnfileiradoEmUtc"),
            IniciadoEmUtc = ReadDateNullable(r, "IniciadoEmUtc"),
            FinalizadoEmUtc = ReadDateNullable(r, "FinalizadoEmUtc"),
            TentativaAtual = r.GetInt32(r.GetOrdinal("TentativaAtual")),
            TentativasMaximas = r.GetInt32(r.GetOrdinal("TentativasMaximas")),
            CategoriaFalha = ReadNullableEnum<AncorarPdfFilaFalhaCategoria>(r, "CategoriaFalha"),
            ErroCodigo = ReadNullableString(r, "ErroCodigo"),
            ErroDetalhe = ReadNullableString(r, "ErroDetalhe"),
            CanceladoPorNome = ReadNullableString(r, "CanceladoPorNome"),
            CanceladoEmUtc = ReadDateNullable(r, "CanceladoEmUtc"),
            CorrelationId = r.GetString(r.GetOrdinal("CorrelationId")),
            CriadoEmUtc = ReadDate(r, "CriadoEmUtc")
        };
    }

    private DateTime ReadDate(SqliteDataReader r, string col)
    {
        var raw = r.GetString(r.GetOrdinal(col));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfExecucaoFilaRepository)}.{col}: '{raw}'.");
    }

    private static DateTime? ReadDateNullable(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfExecucaoFilaRepository)}.{col}: '{raw}'.");
    }

    private static string? ReadNullableString(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static T? ReadNullableEnum<T>(SqliteDataReader r, string col) where T : struct, Enum
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        return Enum.TryParse<T>(raw, out var val) ? val : null;
    }
}
