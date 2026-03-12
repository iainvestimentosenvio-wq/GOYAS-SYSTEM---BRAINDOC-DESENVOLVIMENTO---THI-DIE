using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Tarefas.Telemetry;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteAncorarPdfExecucaoRepository : IAncorarPdfExecucaoRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly SqliteDb _db;
    private readonly TimeProvider _timeProvider;

    public SqliteAncorarPdfExecucaoRepository(SqliteDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AncorarPdfReservaIdempotencia TentarReservarProcessamento(
        int tarefaId,
        string cicloId,
        string nomeEsperadoLogico,
        string arquivoHash,
        string arquivoPath,
        long tamanhoBytes,
        DateTime mtimeUtc)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        // INSERT ... ON CONFLICT DO NOTHING: atomico e idempotente.
        // Retorna 1 se inseriu (reservado), 0 se ja existia (nao reservado).
        cmd.CommandText = @"
INSERT INTO AncorarPdfArquivosProcessadosCiclo (
  TarefaId, CicloId, NomeEsperadoLogico,
  ArquivoHash, ArquivoPath, TamanhoBytes, MtimeUtc, ProcessadoEmUtc
)
VALUES (
  $tarefaId, $cicloId, $nomeEsperadoLogico,
  $arquivoHash, $arquivoPath, $tamanhoBytes, $mtimeUtc, $processadoEmUtc
)
ON CONFLICT(TarefaId, CicloId, NomeEsperadoLogico) DO NOTHING;";

        cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("$cicloId", cicloId);
        cmd.Parameters.AddWithValue("$nomeEsperadoLogico", nomeEsperadoLogico);
        cmd.Parameters.AddWithValue("$arquivoHash", arquivoHash);
        cmd.Parameters.AddWithValue("$arquivoPath", arquivoPath);
        cmd.Parameters.AddWithValue("$tamanhoBytes", tamanhoBytes);
        cmd.Parameters.AddWithValue("$mtimeUtc", mtimeUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$processadoEmUtc", _timeProvider.GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture));

        var linhasAfetadas = cmd.ExecuteNonQuery();
        var reservado = linhasAfetadas > 0;

        return new AncorarPdfReservaIdempotencia(reservado, nomeEsperadoLogico, arquivoHash, arquivoPath);
    }

    public bool CriarExecucao(TarefaAncorarPdfExecucao execucao)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
INSERT INTO AncorarPdfExecucoes (
  ExecucaoId, TarefaId, ClienteId, EsteiraId, CicloId,
  JanelaAlvoUtc, IniciadaEmUtc, FinalizadaEmUtc,
  Status, ErroCodigo, ErroDetalhe, ExecutadaComAtraso,
  ProgramadoPorUserId, ProgramadoPorNome, ProgramadoEmUtc,
  ExecutadoPorUserId, CorrelationId, CriadoEmUtc
)
VALUES (
  $execucaoId, $tarefaId, $clienteId, $esteiraId, $cicloId,
  $janelaAlvoUtc, $iniciadaEmUtc, $finalizadaEmUtc,
  $status, $erroCodigo, $erroDetalhe, $executadaComAtraso,
  $programadoPorUserId, $programadoPorNome, $programadoEmUtc,
  $executadoPorUserId, $correlationId, $criadoEmUtc
)
ON CONFLICT(ExecucaoId) DO NOTHING;";

        BindExecucao(cmd, execucao);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void AtualizarStatus(
        string execucaoId,
        string novoStatus,
        DateTime? iniciadaEmUtc = null,
        DateTime? finalizadaEmUtc = null,
        string? erroCodigo = null,
        string? erroDetalhe = null)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
UPDATE AncorarPdfExecucoes
SET Status = $status,
    IniciadaEmUtc = COALESCE($iniciadaEmUtc, IniciadaEmUtc),
    FinalizadaEmUtc = COALESCE($finalizadaEmUtc, FinalizadaEmUtc),
    ErroCodigo = $erroCodigo,
    ErroDetalhe = $erroDetalhe
WHERE ExecucaoId = $execucaoId;";

        cmd.Parameters.AddWithValue("$execucaoId", execucaoId);
        cmd.Parameters.AddWithValue("$status", novoStatus);
        cmd.Parameters.AddWithValue("$iniciadaEmUtc", (object?)iniciadaEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finalizadaEmUtc", (object?)finalizadaEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$erroCodigo", (object?)erroCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$erroDetalhe", (object?)erroDetalhe ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void SalvarExecucaoCompleta(AncorarPdfSalvarExecucaoEntrada entrada)
    {
        using var activity = AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.persistence.execucao.save_completa", ActivityKind.Internal);
        if (activity != null && activity.IsAllDataRequested)
        {
            activity.SetTag("db.system", "sqlite");
            activity.SetTag("ancorar_pdf.tarefa_id", entrada.Execucao.TarefaId);
        }

        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        // 1) Upsert de status final da execucao.
        using (var cmdExec = connection.CreateCommand())
        {
            cmdExec.Transaction = tx;
            cmdExec.CommandText = @"
INSERT INTO AncorarPdfExecucoes (
  ExecucaoId, TarefaId, ClienteId, EsteiraId, CicloId,
  JanelaAlvoUtc, IniciadaEmUtc, FinalizadaEmUtc,
  Status, ErroCodigo, ErroDetalhe, ExecutadaComAtraso,
  ProgramadoPorUserId, ProgramadoPorNome, ProgramadoEmUtc,
  ExecutadoPorUserId, CorrelationId, CriadoEmUtc
)
VALUES (
  $execucaoId, $tarefaId, $clienteId, $esteiraId, $cicloId,
  $janelaAlvoUtc, $iniciadaEmUtc, $finalizadaEmUtc,
  $status, $erroCodigo, $erroDetalhe, $executadaComAtraso,
  $programadoPorUserId, $programadoPorNome, $programadoEmUtc,
  $executadoPorUserId, $correlationId, $criadoEmUtc
)
ON CONFLICT(ExecucaoId) DO UPDATE SET
  Status = excluded.Status,
  IniciadaEmUtc = COALESCE(excluded.IniciadaEmUtc, AncorarPdfExecucoes.IniciadaEmUtc),
  FinalizadaEmUtc = excluded.FinalizadaEmUtc,
  ErroCodigo = excluded.ErroCodigo,
  ErroDetalhe = excluded.ErroDetalhe;";

            BindExecucao(cmdExec, entrada.Execucao);
            cmdExec.ExecuteNonQuery();
        }

        // 2) Inserir resultados de variaveis (ON CONFLICT DO NOTHING garante idempotencia).
        foreach (var resultado in entrada.Resultados)
        {
            using var cmdRes = connection.CreateCommand();
            cmdRes.Transaction = tx;
            cmdRes.CommandText = @"
INSERT INTO AncorarPdfResultadosVariavel (
  ResultadoId, ExecucaoId, TarefaId, ClienteId,
  ArquivoPath, ArquivoHash,
  Chave, ValorBruto, ValorNormalizado,
  Tipo, CorTemplate, Confianca, Pagina, BboxRelativoJson
)
VALUES (
  $resultadoId, $execucaoId, $tarefaId, $clienteId,
  $arquivoPath, $arquivoHash,
  $chave, $valorBruto, $valorNormalizado,
  $tipo, $corTemplate, $confianca, $pagina, $bboxRelativoJson
)
ON CONFLICT(ResultadoId) DO NOTHING;";

            BindResultado(cmdRes, resultado);
            cmdRes.ExecuteNonQuery();
        }

        // 3) Inserir saida consolidada (ON CONFLICT DO NOTHING — nunca sobrescrever).
        if (entrada.Saida is not null)
        {
            using var cmdSaida = connection.CreateCommand();
            cmdSaida.Transaction = tx;
            cmdSaida.CommandText = @"
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

            BindSaida(cmdSaida, entrada.Saida);
            cmdSaida.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public TarefaAncorarPdfExecucao? ObterPorExecucaoId(string execucaoId)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM AncorarPdfExecucoes WHERE ExecucaoId = $execucaoId LIMIT 1;";
        cmd.Parameters.AddWithValue("$execucaoId", execucaoId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapExecucao(reader) : null;
    }

    public IReadOnlyList<TarefaAncorarPdfExecucao> Listar(AncorarPdfExecucoesFiltro filtro)
    {
        var clienteId = filtro.ClienteId;
        var limite = Math.Clamp(filtro.Limite, 1, 500);
        var offset = Math.Max(0, filtro.Offset);

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        var where = new System.Text.StringBuilder("WHERE ClienteId = $clienteId");
        cmd.Parameters.AddWithValue("$clienteId", clienteId);

        if (filtro.TarefaId.HasValue)
        {
            where.Append(" AND TarefaId = $tarefaId");
            cmd.Parameters.AddWithValue("$tarefaId", filtro.TarefaId.Value);
        }

        if (filtro.DataInicioUtc.HasValue)
        {
            where.Append(" AND JanelaAlvoUtc >= $dataInicio");
            cmd.Parameters.AddWithValue("$dataInicio", filtro.DataInicioUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        if (filtro.DataFimUtc.HasValue)
        {
            where.Append(" AND JanelaAlvoUtc <= $dataFim");
            cmd.Parameters.AddWithValue("$dataFim", filtro.DataFimUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Status))
        {
            where.Append(" AND Status = $status");
            cmd.Parameters.AddWithValue("$status", filtro.Status);
        }

        cmd.CommandText = $@"
SELECT * FROM AncorarPdfExecucoes
{where}
ORDER BY JanelaAlvoUtc DESC
LIMIT $limite OFFSET $offset;";
        cmd.Parameters.AddWithValue("$limite", limite);
        cmd.Parameters.AddWithValue("$offset", offset);

        var resultado = new List<TarefaAncorarPdfExecucao>(limite);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            resultado.Add(MapExecucao(reader));

        return resultado;
    }

    public IReadOnlyList<TarefaAncorarPdfExecucao> ListarPendentesParaExecucao(int clienteId, int limite = 10)
    {
        var limiteNorm = Math.Clamp(limite, 1, 100);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        // SQLite nao suporta FOR UPDATE SKIP LOCKED — usa ORDER BY janela + limite pequeno.
        // Em ambiente multi-instancia SQLite, a serializacao e garantida pelo WAL + escritores unicos.
        cmd.CommandText = @"
SELECT * FROM AncorarPdfExecucoes
WHERE ClienteId = $clienteId
  AND Status IN ('Agendada', 'Executando')
ORDER BY JanelaAlvoUtc ASC
LIMIT $limite;";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);
        cmd.Parameters.AddWithValue("$limite", limiteNorm);

        var resultado = new List<TarefaAncorarPdfExecucao>(limiteNorm);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            resultado.Add(MapExecucao(reader));

        return resultado;
    }

    public bool ArquivoJaProcessado(int tarefaId, string arquivoHash, string cicloId)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT 1 FROM AncorarPdfArquivosProcessadosCiclo
WHERE TarefaId = $tarefaId
  AND ArquivoHash = $arquivoHash
  AND CicloId = $cicloId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("$arquivoHash", arquivoHash);
        cmd.Parameters.AddWithValue("$cicloId", cicloId);
        return cmd.ExecuteScalar() is not null;
    }

    private static void BindExecucao(SqliteCommand cmd, TarefaAncorarPdfExecucao e)
    {
        cmd.Parameters.AddWithValue("$execucaoId", e.ExecucaoId);
        cmd.Parameters.AddWithValue("$tarefaId", e.TarefaId);
        cmd.Parameters.AddWithValue("$clienteId", e.ClienteId);
        cmd.Parameters.AddWithValue("$esteiraId", e.EsteiraId);
        cmd.Parameters.AddWithValue("$cicloId", e.CicloId);
        cmd.Parameters.AddWithValue("$janelaAlvoUtc", e.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$iniciadaEmUtc", (object?)e.IniciadaEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finalizadaEmUtc", (object?)e.FinalizadaEmUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", e.Status);
        cmd.Parameters.AddWithValue("$erroCodigo", (object?)e.ErroCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$erroDetalhe", (object?)e.ErroDetalhe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$executadaComAtraso", e.ExecutadaComAtraso ? 1 : 0);
        cmd.Parameters.AddWithValue("$programadoPorUserId", e.ProgramadoPorUserId);
        cmd.Parameters.AddWithValue("$programadoPorNome", e.ProgramadoPorNome);
        cmd.Parameters.AddWithValue("$programadoEmUtc", e.ProgramadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$executadoPorUserId", (object?)e.ExecutadoPorUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$correlationId", e.CorrelationId);
        cmd.Parameters.AddWithValue("$criadoEmUtc", e.CriadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
    }

    private static void BindResultado(SqliteCommand cmd, ResultadoAncoraVariavel r)
    {
        cmd.Parameters.AddWithValue("$resultadoId", r.ResultadoId);
        cmd.Parameters.AddWithValue("$execucaoId", r.ExecucaoId);
        cmd.Parameters.AddWithValue("$tarefaId", r.TarefaId);
        cmd.Parameters.AddWithValue("$clienteId", r.ClienteId);
        cmd.Parameters.AddWithValue("$arquivoPath", r.ArquivoPath);
        cmd.Parameters.AddWithValue("$arquivoHash", r.ArquivoHash);
        cmd.Parameters.AddWithValue("$chave", r.Chave);
        cmd.Parameters.AddWithValue("$valorBruto", r.ValorBruto);
        cmd.Parameters.AddWithValue("$valorNormalizado", r.ValorNormalizado);
        cmd.Parameters.AddWithValue("$tipo", r.Tipo);
        cmd.Parameters.AddWithValue("$corTemplate", r.CorTemplate);
        cmd.Parameters.AddWithValue("$confianca", r.Confianca);
        cmd.Parameters.AddWithValue("$pagina", r.Pagina);
        cmd.Parameters.AddWithValue("$bboxRelativoJson", (object?)r.BboxRelativoJson ?? DBNull.Value);
    }

    private static void BindSaida(SqliteCommand cmd, SaidaVariavelAncorada s)
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

    private TarefaAncorarPdfExecucao MapExecucao(SqliteDataReader r)
    {
        return new TarefaAncorarPdfExecucao
        {
            ExecucaoId = r.GetString(r.GetOrdinal("ExecucaoId")),
            TarefaId = r.GetInt32(r.GetOrdinal("TarefaId")),
            ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
            EsteiraId = r.GetInt32(r.GetOrdinal("EsteiraId")),
            CicloId = r.GetString(r.GetOrdinal("CicloId")),
            JanelaAlvoUtc = ReadDate(r, "JanelaAlvoUtc"),
            IniciadaEmUtc = ReadDateNullable(r, "IniciadaEmUtc"),
            FinalizadaEmUtc = ReadDateNullable(r, "FinalizadaEmUtc"),
            Status = r.GetString(r.GetOrdinal("Status")),
            ErroCodigo = ReadNullableString(r, "ErroCodigo"),
            ErroDetalhe = ReadNullableString(r, "ErroDetalhe"),
            ExecutadaComAtraso = r.GetInt32(r.GetOrdinal("ExecutadaComAtraso")) == 1,
            ProgramadoPorUserId = r.GetInt32(r.GetOrdinal("ProgramadoPorUserId")),
            ProgramadoPorNome = r.GetString(r.GetOrdinal("ProgramadoPorNome")),
            ProgramadoEmUtc = ReadDate(r, "ProgramadoEmUtc"),
            ExecutadoPorUserId = ReadNullableInt(r, "ExecutadoPorUserId"),
            CorrelationId = r.GetString(r.GetOrdinal("CorrelationId")),
            CriadoEmUtc = ReadDate(r, "CriadoEmUtc")
        };
    }

    private DateTime ReadDate(SqliteDataReader r, string col)
    {
        var raw = r.GetString(r.GetOrdinal(col));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfExecucaoRepository)}.{col}: '{raw}'.");
    }

    private static DateTime? ReadDateNullable(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        var raw = r.GetString(ord);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfExecucaoRepository)}.{col}: '{raw}'.");
    }

    private static string? ReadNullableString(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static int? ReadNullableInt(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }
}
