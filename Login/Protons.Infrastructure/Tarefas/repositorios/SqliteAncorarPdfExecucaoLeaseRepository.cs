using System.Globalization;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteAncorarPdfExecucaoLeaseRepository : IAncorarPdfExecucaoLeaseRepository
{
    private readonly SqliteDb _db;
    private readonly TimeProvider _timeProvider;

    public SqliteAncorarPdfExecucaoLeaseRepository(SqliteDb db, TimeProvider? timeProvider = null)
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
        // ON CONFLICT na coluna FilaItemId (índice único parcial WHERE Ativa=1)
        // garante exclusividade: apenas 1 lease ativo por FilaItemId.
        cmd.CommandText = @"
INSERT INTO AncorarPdfExecucaoLease (
    LeaseId, FilaItemId, TarefaId, ClienteId, WorkerId,
    AcquiredAtUtc, ExpiresAtUtc, Ativa
) VALUES (
    $leaseId, $filaItemId, $tarefaId, $clienteId, $workerId,
    $acquiredAtUtc, $expiresAtUtc, 1
)
ON CONFLICT DO NOTHING;";

        cmd.Parameters.AddWithValue("$leaseId", leaseId);
        cmd.Parameters.AddWithValue("$filaItemId", filaItemId);
        cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("$clienteId", clienteId);
        cmd.Parameters.AddWithValue("$workerId", workerId);
        cmd.Parameters.AddWithValue("$acquiredAtUtc",
            now.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$expiresAtUtc",
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
UPDATE AncorarPdfExecucaoLease
SET Ativa = 0,
    MotivoLiberacao = $motivo,
    LiberadaEmUtc = $liberadaEmUtc
WHERE LeaseId = $leaseId AND Ativa = 1;";
        cmd.Parameters.AddWithValue("$leaseId", leaseId);
        cmd.Parameters.AddWithValue("$motivo", motivo);
        cmd.Parameters.AddWithValue("$liberadaEmUtc",
            now.ToString("o", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AncorarPdfExecucaoLease> ListarExpirados(DateTime referenciaUtc, int limite = 50)
    {
        var lim = Math.Clamp(limite, 1, 500);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM AncorarPdfExecucaoLease
WHERE Ativa = 1 AND ExpiresAtUtc < $referencia
ORDER BY ExpiresAtUtc ASC
LIMIT $limite;";
        cmd.Parameters.AddWithValue("$referencia",
            referenciaUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$limite", lim);

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
UPDATE AncorarPdfExecucaoLease
SET ExpiresAtUtc = $novaExpiracao
WHERE LeaseId = $leaseId AND Ativa = 1;";
        cmd.Parameters.AddWithValue("$leaseId", leaseId);
        cmd.Parameters.AddWithValue("$novaExpiracao",
            novaExpiracao.ToString("o", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery() > 0;
    }

    private AncorarPdfExecucaoLease MapLease(SqliteDataReader r)
    {
        return new AncorarPdfExecucaoLease
        {
            LeaseId = r.GetString(r.GetOrdinal("LeaseId")),
            FilaItemId = r.GetString(r.GetOrdinal("FilaItemId")),
            TarefaId = r.GetInt32(r.GetOrdinal("TarefaId")),
            ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
            WorkerId = r.GetString(r.GetOrdinal("WorkerId")),
            AcquiredAtUtc = ReadDate(r, "AcquiredAtUtc"),
            ExpiresAtUtc = ReadDate(r, "ExpiresAtUtc"),
            Ativa = r.GetInt32(r.GetOrdinal("Ativa")) == 1,
            MotivoLiberacao = ReadNullableString(r, "MotivoLiberacao"),
            LiberadaEmUtc = ReadDateNullable(r, "LiberadaEmUtc")
        };
    }

    private DateTime ReadDate(SqliteDataReader r, string col)
    {
        var raw = r.GetString(r.GetOrdinal(col));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfExecucaoLeaseRepository)}.{col}: '{raw}'.");
    }

    private static DateTime? ReadDateNullable(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfExecucaoLeaseRepository)}.{col}: '{raw}'.");
    }

    private static string? ReadNullableString(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }
}
