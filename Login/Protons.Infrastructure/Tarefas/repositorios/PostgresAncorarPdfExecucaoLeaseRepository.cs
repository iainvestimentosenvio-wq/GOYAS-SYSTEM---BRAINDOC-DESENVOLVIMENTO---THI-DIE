using System.Globalization;
using Npgsql;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class PostgresAncorarPdfExecucaoLeaseRepository : IAncorarPdfExecucaoLeaseRepository
{
    private readonly PostgresDb _db;
    private readonly TimeProvider _timeProvider;

    public PostgresAncorarPdfExecucaoLeaseRepository(PostgresDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AncorarPdfExecucaoLease? TentarAcquirir(
        string filaItemId, int tarefaId, int clienteId, string workerId, TimeSpan duracao)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.Add(duracao);
        var leaseId = Guid.NewGuid().ToString("N");

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        // ON CONFLICT no índice único parcial WHERE ativa=TRUE garante exclusividade.
        cmd.CommandText = @"
INSERT INTO ancorarpdfexecucaolease (
    leaseid, filaitemid, tarefaid, clienteid, workerid,
    acquiredatutc, expiresatutc, ativa
) VALUES (
    @leaseId, @filaItemId, @tarefaId, @clienteId, @workerId,
    @acquiredAtUtc, @expiresAtUtc, TRUE
)
ON CONFLICT DO NOTHING;";

        cmd.Parameters.AddWithValue("leaseId", leaseId);
        cmd.Parameters.AddWithValue("filaItemId", filaItemId);
        cmd.Parameters.AddWithValue("tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("clienteId", clienteId);
        cmd.Parameters.AddWithValue("workerId", workerId);
        cmd.Parameters.AddWithValue("acquiredAtUtc",
            now.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("expiresAtUtc",
            expires.ToString("o", CultureInfo.InvariantCulture));

        var rows = cmd.ExecuteNonQuery();
        if (rows == 0) return null;

        return new AncorarPdfExecucaoLease
        {
            LeaseId = leaseId,
            FilaItemId = filaItemId,
            TarefaId = tarefaId,
            ClienteId = clienteId,
            WorkerId = workerId,
            AcquiredAtUtc = now,
            ExpiresAtUtc = expires,
            Ativa = true
        };
    }

    public void Liberar(string leaseId, string motivo)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE ancorarpdfexecucaolease
SET ativa = FALSE,
    motivoliberacao = @motivo,
    liberadaemutc = @liberadaEmUtc
WHERE leaseid = @leaseId AND ativa = TRUE;";
        cmd.Parameters.AddWithValue("leaseId", leaseId);
        cmd.Parameters.AddWithValue("motivo", motivo);
        cmd.Parameters.AddWithValue("liberadaEmUtc",
            now.ToString("o", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AncorarPdfExecucaoLease> ListarExpirados(DateTime referenciaUtc, int limite = 50)
    {
        var lim = Math.Clamp(limite, 1, 500);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM ancorarpdfexecucaolease
WHERE ativa = TRUE AND expiresatutc < @referencia
ORDER BY expiresatutc ASC
LIMIT @limite;";
        cmd.Parameters.AddWithValue("referencia",
            referenciaUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("limite", lim);

        var result = new List<AncorarPdfExecucaoLease>(lim);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapLease(reader));
        return result;
    }

    public bool TentarRenovar(string leaseId, TimeSpan novaDuracao, DateTime referenciaUtc)
    {
        var novaExpiracao = referenciaUtc.Add(novaDuracao);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE ancorarpdfexecucaolease
SET expiresatutc = @novaExpiracao
WHERE leaseid = @leaseId AND ativa = TRUE;";
        cmd.Parameters.AddWithValue("leaseId", leaseId);
        cmd.Parameters.AddWithValue("novaExpiracao",
            novaExpiracao.ToString("o", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery() > 0;
    }

    private AncorarPdfExecucaoLease MapLease(NpgsqlDataReader r)
    {
        return new AncorarPdfExecucaoLease
        {
            LeaseId = r.GetString(r.GetOrdinal("leaseid")),
            FilaItemId = r.GetString(r.GetOrdinal("filaitemid")),
            TarefaId = r.GetInt32(r.GetOrdinal("tarefaid")),
            ClienteId = r.GetInt32(r.GetOrdinal("clienteid")),
            WorkerId = r.GetString(r.GetOrdinal("workerid")),
            AcquiredAtUtc = ReadDate(r, "acquiredatutc"),
            ExpiresAtUtc = ReadDate(r, "expiresatutc"),
            Ativa = r.GetBoolean(r.GetOrdinal("ativa")),
            MotivoLiberacao = ReadNullableString(r, "motivoliberacao"),
            LiberadaEmUtc = ReadDateNullable(r, "liberadaemutc")
        };
    }

    private DateTime ReadDate(NpgsqlDataReader r, string col)
    {
        var raw = r.GetString(r.GetOrdinal(col));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(PostgresAncorarPdfExecucaoLeaseRepository)}.{col}: '{raw}'.");
    }

    private static DateTime? ReadDateNullable(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(PostgresAncorarPdfExecucaoLeaseRepository)}.{col}: '{raw}'.");
    }

    private static string? ReadNullableString(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }
}
