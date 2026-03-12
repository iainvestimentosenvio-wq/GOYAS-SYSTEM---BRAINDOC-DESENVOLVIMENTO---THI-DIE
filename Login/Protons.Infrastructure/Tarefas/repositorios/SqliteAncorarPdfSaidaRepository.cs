using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Tarefas.Telemetry;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteAncorarPdfSaidaRepository : IAncorarPdfSaidaRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly SqliteDb _db;
    private readonly TimeProvider _timeProvider;

    public SqliteAncorarPdfSaidaRepository(SqliteDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool Salvar(SaidaVariavelAncorada saida)
    {
        using var activity = AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.persistence.saida.save", ActivityKind.Internal);
        if (activity != null && activity.IsAllDataRequested)
        {
            activity.SetTag("db.system", "sqlite");
            activity.SetTag("ancorar_pdf.tarefa_id", saida.TarefaId);
        }

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        // ON CONFLICT DO NOTHING: nunca sobrescrever payload antigo.
        cmd.CommandText = @"
INSERT INTO AncorarPdfSaidaVariavel (
  SaidaId, ExecucaoId, TarefaId, ClienteId,
  ArquivoPath, ArquivoHash, ArquivoNomeLogico,
  DataExecucaoUtc, SchemaVersion, VariaveisJson, PayloadHashSha256, CriadoEmUtc
)
VALUES (
  $saidaId, $execucaoId, $tarefaId, $clienteId,
  $arquivoPath, $arquivoHash, $arquivoNomeLogico,
  $dataExecucaoUtc, $schemaVersion, $variaveisJson, $payloadHashSha256, $criadoEmUtc
)
ON CONFLICT DO NOTHING;";

        Bind(cmd, saida);
        return cmd.ExecuteNonQuery() > 0;
    }

    public SaidaVariavelAncorada? ObterPorSaidaId(string saidaId)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM AncorarPdfSaidaVariavel WHERE SaidaId = $saidaId LIMIT 1;";
        cmd.Parameters.AddWithValue("$saidaId", saidaId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<SaidaVariavelAncorada> ListarPorExecucaoId(string execucaoId)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM AncorarPdfSaidaVariavel
WHERE ExecucaoId = $execucaoId
ORDER BY CriadoEmUtc ASC;";
        cmd.Parameters.AddWithValue("$execucaoId", execucaoId);

        var resultado = new List<SaidaVariavelAncorada>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            resultado.Add(Map(reader));

        return resultado;
    }

    public IReadOnlyList<SaidaVariavelAncorada> Listar(AncorarPdfSaidasFiltro filtro)
    {
        var limite = Math.Clamp(filtro.Limite, 1, 500);
        var offset = Math.Max(0, filtro.Offset);

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        var where = new System.Text.StringBuilder("WHERE ClienteId = $clienteId");
        cmd.Parameters.AddWithValue("$clienteId", filtro.ClienteId);

        if (filtro.TarefaId.HasValue)
        {
            where.Append(" AND TarefaId = $tarefaId");
            cmd.Parameters.AddWithValue("$tarefaId", filtro.TarefaId.Value);
        }

        if (filtro.DataInicioUtc.HasValue)
        {
            where.Append(" AND DataExecucaoUtc >= $dataInicio");
            cmd.Parameters.AddWithValue("$dataInicio", filtro.DataInicioUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        if (filtro.DataFimUtc.HasValue)
        {
            where.Append(" AND DataExecucaoUtc <= $dataFim");
            cmd.Parameters.AddWithValue("$dataFim", filtro.DataFimUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        cmd.CommandText = $@"
SELECT * FROM AncorarPdfSaidaVariavel
{where}
ORDER BY DataExecucaoUtc DESC
LIMIT $limite OFFSET $offset;";
        cmd.Parameters.AddWithValue("$limite", limite);
        cmd.Parameters.AddWithValue("$offset", offset);

        var resultado = new List<SaidaVariavelAncorada>(limite);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            resultado.Add(Map(reader));

        return resultado;
    }

    public bool ExisteSaidaComHash(int tarefaId, string payloadHashSha256)
    {
        using var activity = AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.persistence.saida.exists_hash", ActivityKind.Internal);
        if (activity != null && activity.IsAllDataRequested)
        {
            activity.SetTag("db.system", "sqlite");
            activity.SetTag("ancorar_pdf.tarefa_id", tarefaId);
        }

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT 1 FROM AncorarPdfSaidaVariavel
WHERE TarefaId = $tarefaId AND PayloadHashSha256 = $hash AND SchemaVersion = 1
LIMIT 1;";
        cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("$hash", payloadHashSha256);
        return cmd.ExecuteScalar() is not null;
    }

    private static void Bind(SqliteCommand cmd, SaidaVariavelAncorada s)
    {
        var variaveisJson = JsonSerializer.Serialize(s.Variaveis, SerializerOptions);
        cmd.Parameters.AddWithValue("$saidaId", s.SaidaId);
        cmd.Parameters.AddWithValue("$execucaoId", s.ExecucaoId);
        cmd.Parameters.AddWithValue("$tarefaId", s.TarefaId);
        cmd.Parameters.AddWithValue("$clienteId", s.ClienteId);
        cmd.Parameters.AddWithValue("$arquivoPath", s.ArquivoPath);
        cmd.Parameters.AddWithValue("$arquivoHash", s.ArquivoHash);
        cmd.Parameters.AddWithValue("$arquivoNomeLogico", s.ArquivoNomeLogico);
        cmd.Parameters.AddWithValue("$dataExecucaoUtc", s.DataExecucaoUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$schemaVersion", s.SchemaVersion);
        cmd.Parameters.AddWithValue("$variaveisJson", variaveisJson);
        cmd.Parameters.AddWithValue("$payloadHashSha256", s.PayloadHashSha256);
        cmd.Parameters.AddWithValue("$criadoEmUtc", s.CriadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
    }

    private SaidaVariavelAncorada Map(SqliteDataReader r)
    {
        var variaveisJson = r.GetString(r.GetOrdinal("VariaveisJson"));
        var variaveis = JsonSerializer.Deserialize<IReadOnlyList<SaidaVariavelItem>>(variaveisJson, SerializerOptions) ?? [];

        return new SaidaVariavelAncorada
        {
            SaidaId = r.GetString(r.GetOrdinal("SaidaId")),
            ExecucaoId = r.GetString(r.GetOrdinal("ExecucaoId")),
            TarefaId = r.GetInt32(r.GetOrdinal("TarefaId")),
            ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
            ArquivoPath = r.GetString(r.GetOrdinal("ArquivoPath")),
            ArquivoHash = r.GetString(r.GetOrdinal("ArquivoHash")),
            ArquivoNomeLogico = r.GetString(r.GetOrdinal("ArquivoNomeLogico")),
            DataExecucaoUtc = ReadDate(r, "DataExecucaoUtc"),
            SchemaVersion = r.GetInt32(r.GetOrdinal("SchemaVersion")),
            Variaveis = variaveis,
            PayloadHashSha256 = r.GetString(r.GetOrdinal("PayloadHashSha256")),
            CriadoEmUtc = ReadDate(r, "CriadoEmUtc")
        };
    }

    private DateTime ReadDate(SqliteDataReader r, string col)
    {
        var raw = r.GetString(r.GetOrdinal(col));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfSaidaRepository)}.{col}: '{raw}'.");
    }
}
