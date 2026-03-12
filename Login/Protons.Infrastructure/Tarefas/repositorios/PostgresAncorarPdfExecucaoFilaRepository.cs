using System.Globalization;
using Npgsql;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class PostgresAncorarPdfExecucaoFilaRepository : IAncorarPdfFilaExecucaoRepository
{
    private readonly PostgresDb _db;
    private readonly TimeProvider _timeProvider;

    public PostgresAncorarPdfExecucaoFilaRepository(PostgresDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AncorarPdfFilaEnfileirarResultado Enfileirar(AncorarPdfFilaItem item)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ancorarpdfexecucaofila (
    filaitemid, tarefaid, clienteid, cicloid, janelaalvoutc,
    prioridadeexecucao, status, motivo,
    enfileiradoporuserid, enfileiradopornome, enfileiradoemutc,
    tentativaatual, tentativasmaximas, correlationid, criadoemutc
) VALUES (
    @filaItemId, @tarefaId, @clienteId, @cicloId, @janelaAlvoUtc,
    @prioridadeExecucao, @status, @motivo,
    @enfileiradoPorUserId, @enfileiradoPorNome, @enfileiradoEmUtc,
    @tentativaAtual, @tentativasMaximas, @correlationId, @criadoEmUtc
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
        // Postgres: FOR UPDATE SKIP LOCKED dentro de transação para exclusividade multi-instância.
        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        AncorarPdfFilaItem? candidato = null;
        using (var cmdSel = connection.CreateCommand())
        {
            cmdSel.Transaction = tx;
            cmdSel.CommandText = @"
SELECT * FROM ancorarpdfexecucaofila
WHERE status = 'Aguardando'
ORDER BY prioridadeexecucao ASC, enfileiradoemutc ASC
LIMIT 1
FOR UPDATE SKIP LOCKED;";
            using var reader = cmdSel.ExecuteReader();
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
UPDATE ancorarpdfexecucaofila
SET status = 'EmProcessamento'
WHERE filaitemid = @id AND status = 'Aguardando';";
            cmdUpd.Parameters.AddWithValue("id", candidato.FilaItemId);
            cmdUpd.ExecuteNonQuery();
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
UPDATE ancorarpdfexecucaofila
SET status = @status,
    iniciadoemutc = COALESCE(@iniciadoEmUtc, iniciadoemutc),
    finalizadoemutc = COALESCE(@finalizadoEmUtc, finalizadoemutc),
    categoriafalha = COALESCE(@categoriaFalha, categoriafalha),
    errocodigo = COALESCE(@erroCodigo, errocodigo),
    errodetalhe = COALESCE(@erroDetalhe, errodetalhe),
    tentativaatual = COALESCE(@tentativaAtual, tentativaatual)
WHERE filaitemid = @filaItemId;";

        cmd.Parameters.AddWithValue("filaItemId", filaItemId);
        cmd.Parameters.AddWithValue("status", status.ToString());
        cmd.Parameters.AddWithValue("iniciadoEmUtc",
            (object?)iniciadoEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("finalizadoEmUtc",
            (object?)finalizadoEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("categoriaFalha",
            (object?)categoriaFalha?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("erroCodigo", (object?)erroCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("erroDetalhe", (object?)erroDetalhe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tentativaAtual", (object?)tentativaAtual ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool TentarCancelar(string filaItemId, string canceladoPorNome, DateTime canceladoEmUtc)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE ancorarpdfexecucaofila
SET status = 'Cancelado',
    canceladopornome = @canceladoPorNome,
    canceladoemutc = @canceladoEmUtc,
    finalizadoemutc = @canceladoEmUtc
WHERE filaitemid = @filaItemId AND status = 'Aguardando';";

        cmd.Parameters.AddWithValue("filaItemId", filaItemId);
        cmd.Parameters.AddWithValue("canceladoPorNome", canceladoPorNome);
        cmd.Parameters.AddWithValue("canceladoEmUtc",
            canceladoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<AncorarPdfFilaItem> ListarParaReidratar(int limite = 200)
    {
        var lim = Math.Clamp(limite, 1, 1000);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT f.* FROM ancorarpdfexecucaofila f
LEFT JOIN ancorarpdfexecucaolease l
    ON l.filaitemid = f.filaitemid AND l.ativa = TRUE
WHERE f.status = 'Aguardando' AND l.leaseid IS NULL
ORDER BY f.prioridadeexecucao ASC, f.enfileiradoemutc ASC
LIMIT @limite;";
        cmd.Parameters.AddWithValue("limite", lim);
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
            where.Append(" AND clienteid = @clienteId");
            cmd.Parameters.AddWithValue("clienteId", filtro.ClienteId.Value);
        }
        if (filtro.TarefaId.HasValue)
        {
            where.Append(" AND tarefaid = @tarefaId");
            cmd.Parameters.AddWithValue("tarefaId", filtro.TarefaId.Value);
        }
        if (filtro.Status.HasValue)
        {
            where.Append(" AND status = @status");
            cmd.Parameters.AddWithValue("status", filtro.Status.Value.ToString());
        }
        if (filtro.DataInicioUtc.HasValue)
        {
            where.Append(" AND enfileiradoemutc >= @dataInicio");
            cmd.Parameters.AddWithValue("dataInicio",
                filtro.DataInicioUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }
        if (filtro.DataFimUtc.HasValue)
        {
            where.Append(" AND enfileiradoemutc <= @dataFim");
            cmd.Parameters.AddWithValue("dataFim",
                filtro.DataFimUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        cmd.CommandText = $@"
SELECT * FROM ancorarpdfexecucaofila
{where}
ORDER BY enfileiradoemutc DESC
LIMIT @limite OFFSET @offset;";
        cmd.Parameters.AddWithValue("limite", limite);
        cmd.Parameters.AddWithValue("offset", offset);

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
        cmd.CommandText = "SELECT COUNT(*) FROM ancorarpdfexecucaofila WHERE status = 'Aguardando';";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static void BindItem(NpgsqlCommand cmd, AncorarPdfFilaItem item)
    {
        cmd.Parameters.AddWithValue("filaItemId", item.FilaItemId);
        cmd.Parameters.AddWithValue("tarefaId", item.TarefaId);
        cmd.Parameters.AddWithValue("clienteId", item.ClienteId);
        cmd.Parameters.AddWithValue("cicloId", item.CicloId);
        cmd.Parameters.AddWithValue("janelaAlvoUtc",
            item.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("prioridadeExecucao", item.PrioridadeExecucao);
        cmd.Parameters.AddWithValue("status", item.Status.ToString());
        cmd.Parameters.AddWithValue("motivo", item.Motivo);
        cmd.Parameters.AddWithValue("enfileiradoPorUserId", item.EnfileiradoPorUserId);
        cmd.Parameters.AddWithValue("enfileiradoPorNome", item.EnfileiradoPorNome);
        cmd.Parameters.AddWithValue("enfileiradoEmUtc",
            item.EnfileiradoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("tentativaAtual", item.TentativaAtual);
        cmd.Parameters.AddWithValue("tentativasMaximas", item.TentativasMaximas);
        cmd.Parameters.AddWithValue("correlationId", item.CorrelationId);
        cmd.Parameters.AddWithValue("criadoEmUtc",
            item.CriadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
    }

    private AncorarPdfFilaItem MapItem(NpgsqlDataReader r)
    {
        return new AncorarPdfFilaItem
        {
            FilaItemId = r.GetString(r.GetOrdinal("filaitemid")),
            TarefaId = r.GetInt32(r.GetOrdinal("tarefaid")),
            ClienteId = r.GetInt32(r.GetOrdinal("clienteid")),
            CicloId = r.GetString(r.GetOrdinal("cicloid")),
            JanelaAlvoUtc = ReadDate(r, "janelaalvoutc"),
            PrioridadeExecucao = r.GetInt32(r.GetOrdinal("prioridadeexecucao")),
            Status = Enum.Parse<AncorarPdfFilaStatus>(r.GetString(r.GetOrdinal("status"))),
            Motivo = r.GetString(r.GetOrdinal("motivo")),
            EnfileiradoPorUserId = r.GetInt32(r.GetOrdinal("enfileiradoporuserid")),
            EnfileiradoPorNome = r.GetString(r.GetOrdinal("enfileiradopornome")),
            EnfileiradoEmUtc = ReadDate(r, "enfileiradoemutc"),
            IniciadoEmUtc = ReadDateNullable(r, "iniciadoemutc"),
            FinalizadoEmUtc = ReadDateNullable(r, "finalizadoemutc"),
            TentativaAtual = r.GetInt32(r.GetOrdinal("tentativaatual")),
            TentativasMaximas = r.GetInt32(r.GetOrdinal("tentativasmaximas")),
            CategoriaFalha = ReadNullableEnum<AncorarPdfFilaFalhaCategoria>(r, "categoriafalha"),
            ErroCodigo = ReadNullableString(r, "errocodigo"),
            ErroDetalhe = ReadNullableString(r, "errodetalhe"),
            CanceladoPorNome = ReadNullableString(r, "canceladopornome"),
            CanceladoEmUtc = ReadDateNullable(r, "canceladoemutc"),
            CorrelationId = r.GetString(r.GetOrdinal("correlationid")),
            CriadoEmUtc = ReadDate(r, "criadoemutc")
        };
    }

    private DateTime ReadDate(NpgsqlDataReader r, string col)
    {
        var raw = r.GetString(r.GetOrdinal(col));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(PostgresAncorarPdfExecucaoFilaRepository)}.{col}: '{raw}'.");
    }

    private static DateTime? ReadDateNullable(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(PostgresAncorarPdfExecucaoFilaRepository)}.{col}: '{raw}'.");
    }

    private static string? ReadNullableString(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static T? ReadNullableEnum<T>(NpgsqlDataReader r, string col) where T : struct, Enum
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        return Enum.TryParse<T>(raw, out var val) ? val : null;
    }
}
