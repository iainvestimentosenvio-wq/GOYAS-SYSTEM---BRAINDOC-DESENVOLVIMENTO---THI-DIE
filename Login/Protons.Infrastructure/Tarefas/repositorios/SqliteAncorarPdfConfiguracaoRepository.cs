using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class SqliteAncorarPdfConfiguracaoRepository : IAncorarPdfConfiguracaoRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly SqliteDb _db;
    private readonly TimeProvider _timeProvider;

    public SqliteAncorarPdfConfiguracaoRepository(SqliteDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AncorarPdfConfiguracaoTarefa? ObterPorTarefaId(int tarefaId)
    {
        if (tarefaId <= 0)
            return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT *
FROM AncorarPdfConfiguracoesTarefa
WHERE TarefaId = $tarefaId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$tarefaId", tarefaId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Salvar(AncorarPdfConfiguracaoTarefa configuracao, AncorarPdfTemplateHistoricoItem? historicoAlteracao)
    {
        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO AncorarPdfConfiguracoesTarefa (
  TarefaId,
  ClienteId,
  EsteiraId,
  NomeTarefaPersonalizado,
  PastaMonitoradaPath,
  PdfModeloPath,
  NomeReferenciaArquivo,
  MonitorarSubpastas,
  ValidacaoClienteAtiva,
  LimiarSimilaridadeNome,
  HighlightOpacity,
  ModoSelecao,
  PdfModeloCrossCliente,
  PdfModeloCrossClienteJustificativa,
  Recorrencia,
  AgendamentoSegundo,
  TimezoneId,
  PrioridadeExecucao,
  DstHorarioInvalidoPolicy,
  DstHorarioAmbiguoPolicy,
  TemplateAncorasJson,
  OcrFallbackAtivo,
  OcrDpi,
  OcrLang,
  ProgramadoPorUserId,
  ProgramadoPorNome,
  ProgramadoEmUtc,
  AtualizadoPorUserId,
  AtualizadoEmUtc,
  VersaoTemplate
)
VALUES (
  $tarefaId,
  $clienteId,
  $esteiraId,
  $nomeTarefaPersonalizado,
  $pastaMonitoradaPath,
  $pdfModeloPath,
  $nomeReferenciaArquivo,
  $monitorarSubpastas,
  $validacaoClienteAtiva,
  $limiarSimilaridadeNome,
  $highlightOpacity,
  $modoSelecao,
  $pdfModeloCrossCliente,
  $pdfModeloCrossClienteJustificativa,
  $recorrencia,
  $agendamentoSegundo,
  $timezoneId,
  $prioridadeExecucao,
  $dstHorarioInvalidoPolicy,
  $dstHorarioAmbiguoPolicy,
  $templateAncorasJson,
  $ocrFallbackAtivo,
  $ocrDpi,
  $ocrLang,
  $programadoPorUserId,
  $programadoPorNome,
  $programadoEmUtc,
  $atualizadoPorUserId,
  $atualizadoEmUtc,
  $versaoTemplate
)
ON CONFLICT(TarefaId) DO UPDATE SET
  ClienteId = excluded.ClienteId,
  EsteiraId = excluded.EsteiraId,
  NomeTarefaPersonalizado = excluded.NomeTarefaPersonalizado,
  PastaMonitoradaPath = excluded.PastaMonitoradaPath,
  PdfModeloPath = excluded.PdfModeloPath,
  NomeReferenciaArquivo = excluded.NomeReferenciaArquivo,
  MonitorarSubpastas = excluded.MonitorarSubpastas,
  ValidacaoClienteAtiva = excluded.ValidacaoClienteAtiva,
  LimiarSimilaridadeNome = excluded.LimiarSimilaridadeNome,
  HighlightOpacity = excluded.HighlightOpacity,
  ModoSelecao = excluded.ModoSelecao,
  PdfModeloCrossCliente = excluded.PdfModeloCrossCliente,
  PdfModeloCrossClienteJustificativa = excluded.PdfModeloCrossClienteJustificativa,
  Recorrencia = excluded.Recorrencia,
  AgendamentoSegundo = excluded.AgendamentoSegundo,
  TimezoneId = excluded.TimezoneId,
  PrioridadeExecucao = excluded.PrioridadeExecucao,
  DstHorarioInvalidoPolicy = excluded.DstHorarioInvalidoPolicy,
  DstHorarioAmbiguoPolicy = excluded.DstHorarioAmbiguoPolicy,
  TemplateAncorasJson = excluded.TemplateAncorasJson,
  OcrFallbackAtivo = excluded.OcrFallbackAtivo,
  OcrDpi = excluded.OcrDpi,
  OcrLang = excluded.OcrLang,
  ProgramadoPorUserId = excluded.ProgramadoPorUserId,
  ProgramadoPorNome = excluded.ProgramadoPorNome,
  ProgramadoEmUtc = excluded.ProgramadoEmUtc,
  AtualizadoPorUserId = excluded.AtualizadoPorUserId,
  AtualizadoEmUtc = excluded.AtualizadoEmUtc,
  VersaoTemplate = excluded.VersaoTemplate;
";

            Bind(cmd, configuracao);
            cmd.ExecuteNonQuery();
        }

        if (historicoAlteracao is not null)
        {
            using var hist = connection.CreateCommand();
            hist.Transaction = tx;
            hist.CommandText = @"
INSERT INTO AncorarPdfTemplateHistorico (
  TarefaId,
  Versao,
  AntesJson,
  DepoisJson,
  AlteradoPorUserId,
  AlteradoPorNome,
  AlteradoEmUtc
)
VALUES (
  $tarefaId,
  $versao,
  $antesJson,
  $depoisJson,
  $alteradoPorUserId,
  $alteradoPorNome,
  $alteradoEmUtc
);
";
            hist.Parameters.AddWithValue("$tarefaId", historicoAlteracao.TarefaId);
            hist.Parameters.AddWithValue("$versao", historicoAlteracao.Versao);
            hist.Parameters.AddWithValue("$antesJson", historicoAlteracao.AntesJson);
            hist.Parameters.AddWithValue("$depoisJson", historicoAlteracao.DepoisJson);
            hist.Parameters.AddWithValue("$alteradoPorUserId", historicoAlteracao.AlteradoPorUserId);
            hist.Parameters.AddWithValue("$alteradoPorNome", historicoAlteracao.AlteradoPorNome);
            hist.Parameters.AddWithValue("$alteradoEmUtc", historicoAlteracao.AlteradoEmUtc.ToString("o", CultureInfo.InvariantCulture));
            hist.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<AncorarPdfTemplateHistoricoItem> ListarHistoricoTemplate(int tarefaId, int limite)
    {
        var limiteNormalizado = Math.Clamp(limite, 1, 200);
        var itens = new List<AncorarPdfTemplateHistoricoItem>(limiteNormalizado);

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT *
FROM AncorarPdfTemplateHistorico
WHERE TarefaId = $tarefaId
ORDER BY Versao DESC
LIMIT $limite;";
        cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("$limite", limiteNormalizado);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            itens.Add(new AncorarPdfTemplateHistoricoItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                TarefaId = reader.GetInt32(reader.GetOrdinal("TarefaId")),
                Versao = reader.GetInt32(reader.GetOrdinal("Versao")),
                AntesJson = reader.GetString(reader.GetOrdinal("AntesJson")),
                DepoisJson = reader.GetString(reader.GetOrdinal("DepoisJson")),
                AlteradoPorUserId = reader.GetInt32(reader.GetOrdinal("AlteradoPorUserId")),
                AlteradoPorNome = reader.GetString(reader.GetOrdinal("AlteradoPorNome")),
                AlteradoEmUtc = ReadDate(reader, "AlteradoEmUtc")
            });
        }

        return itens;
    }

    public bool ExisteNomeAtivoNoEscopo(int clienteId, int esteiraId, string nomeTarefaPersonalizado, int? tarefaIdIgnorar)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM AncorarPdfConfiguracoesTarefa c
INNER JOIN Tarefas t ON t.Id = c.TarefaId
WHERE c.ClienteId = $clienteId
  AND c.EsteiraId = $esteiraId
  AND lower(c.NomeTarefaPersonalizado) = lower($nomeTarefa)
  AND t.Ativa = 1
  AND ($tarefaIdIgnorar IS NULL OR c.TarefaId <> $tarefaIdIgnorar)
LIMIT 1;";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);
        cmd.Parameters.AddWithValue("$esteiraId", esteiraId);
        cmd.Parameters.AddWithValue("$nomeTarefa", nomeTarefaPersonalizado.Trim());
        cmd.Parameters.AddWithValue("$tarefaIdIgnorar", (object?)tarefaIdIgnorar ?? DBNull.Value);

        return cmd.ExecuteScalar() is not null;
    }

    public IReadOnlyList<AncorarPdfSchedulerAgendamentoAtivo> ListarAgendamentosAtivosAte(DateTime referenciaUtc, int limite)
    {
        var itens = new List<AncorarPdfSchedulerAgendamentoAtivo>(Math.Clamp(limite, 1, 1000));

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT
  c.TarefaId,
  c.ClienteId,
  t.VencimentoUtc,
  t.CriadoEmUtc,
  c.TimezoneId,
  c.PrioridadeExecucao,
  c.DstHorarioInvalidoPolicy,
  c.DstHorarioAmbiguoPolicy
FROM AncorarPdfConfiguracoesTarefa c
INNER JOIN Tarefas t ON t.Id = c.TarefaId
WHERE t.Ativa = 1
  AND t.Status = $statusAgendada
  AND lower(t.FerramentaId) = $ferramentaId
  AND t.VencimentoUtc <= $referenciaUtc
ORDER BY c.PrioridadeExecucao ASC, t.CriadoEmUtc ASC
LIMIT $limite;";
        cmd.Parameters.AddWithValue("$statusAgendada", TarefaStatus.Agendada.ToString());
        cmd.Parameters.AddWithValue("$ferramentaId", FerramentaTarefaIds.AncorarPdfCanonico);
        cmd.Parameters.AddWithValue("$referenciaUtc", referenciaUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 1000));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            itens.Add(new AncorarPdfSchedulerAgendamentoAtivo
            {
                TarefaId = reader.GetInt32(reader.GetOrdinal("TarefaId")),
                ClienteId = reader.GetInt32(reader.GetOrdinal("ClienteId")),
                VencimentoUtc = ReadDate(reader, "VencimentoUtc"),
                CriadoEmUtc = ReadDate(reader, "CriadoEmUtc"),
                TimezoneId = ReadString(reader, "TimezoneId", "UTC"),
                PrioridadeExecucao = ReadInt(reader, "PrioridadeExecucao", 3),
                DstHorarioInvalidoPolicy = ReadDstInvalidoPolicy(reader, "DstHorarioInvalidoPolicy"),
                DstHorarioAmbiguoPolicy = ReadDstAmbiguoPolicy(reader, "DstHorarioAmbiguoPolicy")
            });
        }

        return itens;
    }

    public long RegistrarBacklogPendente(AncorarPdfSchedulerBacklogRegistro registro)
    {
        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = @"
INSERT INTO AncorarPdfSchedulerBacklogPendente (
  TarefaId,
  ClienteId,
  JanelaAlvoUtc,
  AtrasoSegundos,
  Motivo,
  DetectadoEmUtc,
  Status
)
VALUES (
  $tarefaId,
  $clienteId,
  $janelaAlvoUtc,
  $atrasoSegundos,
  $motivo,
  $detectadoEmUtc,
  $status
)
ON CONFLICT(TarefaId, JanelaAlvoUtc) DO NOTHING;";
            insert.Parameters.AddWithValue("$tarefaId", registro.TarefaId);
            insert.Parameters.AddWithValue("$clienteId", registro.ClienteId);
            insert.Parameters.AddWithValue("$janelaAlvoUtc", registro.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$atrasoSegundos", Math.Max(0, registro.AtrasoSegundos));
            insert.Parameters.AddWithValue("$motivo", string.IsNullOrWhiteSpace(registro.Motivo) ? "misfire_offline" : registro.Motivo.Trim());
            insert.Parameters.AddWithValue("$detectadoEmUtc", registro.DetectadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$status", AncorarPdfBacklogStatus.Pendente.ToString());
            insert.ExecuteNonQuery();
        }

        using var select = connection.CreateCommand();
        select.Transaction = tx;
        select.CommandText = @"
SELECT Id
FROM AncorarPdfSchedulerBacklogPendente
WHERE TarefaId = $tarefaId
  AND JanelaAlvoUtc = $janelaAlvoUtc
LIMIT 1;";
        select.Parameters.AddWithValue("$tarefaId", registro.TarefaId);
        select.Parameters.AddWithValue("$janelaAlvoUtc", registro.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
        var id = Convert.ToInt64(select.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        tx.Commit();
        return id;
    }

    public AncorarPdfBacklogPendenteItem? ObterBacklogPendentePorId(long backlogId)
    {
        if (backlogId <= 0)
            return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT *
FROM AncorarPdfSchedulerBacklogPendente
WHERE Id = $id
  AND Status = $status
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", backlogId);
        cmd.Parameters.AddWithValue("$status", AncorarPdfBacklogStatus.Pendente.ToString());

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapBacklog(reader) : null;
    }

    public IReadOnlyList<AncorarPdfBacklogPendenteItem> ListarBacklogPendente(int clienteId, int limite)
    {
        var itens = new List<AncorarPdfBacklogPendenteItem>(Math.Clamp(limite, 1, 500));

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT *
FROM AncorarPdfSchedulerBacklogPendente
WHERE ClienteId = $clienteId
  AND Status = $status
ORDER BY DetectadoEmUtc ASC, Id ASC
LIMIT $limite;";
        cmd.Parameters.AddWithValue("$clienteId", clienteId);
        cmd.Parameters.AddWithValue("$status", AncorarPdfBacklogStatus.Pendente.ToString());
        cmd.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 500));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            itens.Add(MapBacklog(reader));
        }

        return itens;
    }

    public bool ResolverBacklogPendente(
        long backlogId,
        AncorarPdfBacklogStatus statusFinal,
        int decididoPorUserId,
        string decididoPorNome,
        DateTime decididoEmUtc,
        string? observacao)
    {
        if (backlogId <= 0 || statusFinal == AncorarPdfBacklogStatus.Pendente)
            return false;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE AncorarPdfSchedulerBacklogPendente
SET Status = $status,
    DecididoEmUtc = $decididoEmUtc,
    DecididoPorUserId = $decididoPorUserId,
    DecididoPorNome = $decididoPorNome,
    Observacao = $observacao
WHERE Id = $id
  AND Status = $pendente;";
        cmd.Parameters.AddWithValue("$status", statusFinal.ToString());
        cmd.Parameters.AddWithValue("$decididoEmUtc", decididoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$decididoPorUserId", decididoPorUserId);
        cmd.Parameters.AddWithValue("$decididoPorNome", decididoPorNome.Trim());
        cmd.Parameters.AddWithValue("$observacao", (object?)observacao?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", backlogId);
        cmd.Parameters.AddWithValue("$pendente", AncorarPdfBacklogStatus.Pendente.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    public void RegistrarEventoScheduler(AncorarPdfSchedulerEventoRegistro eventoRegistro)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO AncorarPdfSchedulerEventos (
  TarefaId, ClienteId, TipoEvento, Detalhes, OcorreuEmUtc,
  CorrelationId, StatusAnterior, StatusNovo, ErroCodigo, ExecutadaComAtraso
) VALUES (
  $tarefaId, $clienteId, $tipoEvento, $detalhes, $ocorreuEmUtc,
  $correlationId, $statusAnterior, $statusNovo, $erroCodigo, $executadaComAtraso
);";
        cmd.Parameters.AddWithValue("$tarefaId", eventoRegistro.TarefaId);
        cmd.Parameters.AddWithValue("$clienteId", eventoRegistro.ClienteId);
        cmd.Parameters.AddWithValue("$tipoEvento", eventoRegistro.TipoEvento.Trim());
        cmd.Parameters.AddWithValue("$detalhes", (object?)eventoRegistro.Detalhes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ocorreuEmUtc", eventoRegistro.OcorreuEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$correlationId", eventoRegistro.CorrelationId);
        cmd.Parameters.AddWithValue("$statusAnterior", eventoRegistro.StatusAnterior);
        cmd.Parameters.AddWithValue("$statusNovo", eventoRegistro.StatusNovo);
        cmd.Parameters.AddWithValue("$erroCodigo", eventoRegistro.ErroCodigo);
        cmd.Parameters.AddWithValue("$executadaComAtraso", eventoRegistro.ExecutadaComAtraso ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AncorarPdfEventoHistoricoItem> ListarEventosOperacionais(
        AncorarPdfEventoHistoricoFiltro filtro)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        var limite = Math.Clamp(filtro.Limite, 1, 1000);

        var sb = new System.Text.StringBuilder(@"
SELECT Id, TarefaId, ClienteId, TipoEvento, Detalhes, OcorreuEmUtc,
       CorrelationId, StatusAnterior, StatusNovo, ErroCodigo, ExecutadaComAtraso
FROM AncorarPdfSchedulerEventos
WHERE ClienteId = $clienteId");

        cmd.Parameters.AddWithValue("$clienteId", filtro.ClienteId);

        if (filtro.TarefaId.HasValue)
        {
            sb.Append(" AND TarefaId = $tarefaId");
            cmd.Parameters.AddWithValue("$tarefaId", filtro.TarefaId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filtro.TipoEvento))
        {
            sb.Append(" AND TipoEvento = $tipoEvento");
            cmd.Parameters.AddWithValue("$tipoEvento", filtro.TipoEvento.Trim());
        }
        if (filtro.DataInicioUtc.HasValue)
        {
            sb.Append(" AND OcorreuEmUtc >= $dataInicio");
            cmd.Parameters.AddWithValue("$dataInicio", filtro.DataInicioUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }
        if (filtro.DataFimUtc.HasValue)
        {
            sb.Append(" AND OcorreuEmUtc <= $dataFim");
            cmd.Parameters.AddWithValue("$dataFim", filtro.DataFimUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        sb.Append(" ORDER BY OcorreuEmUtc DESC LIMIT $limite");
        cmd.Parameters.AddWithValue("$limite", limite);
        cmd.CommandText = sb.ToString();

        using var reader = cmd.ExecuteReader();
        var result = new List<AncorarPdfEventoHistoricoItem>();
        while (reader.Read())
        {
            result.Add(new AncorarPdfEventoHistoricoItem
            {
                Id = reader.GetInt64(0),
                TarefaId = reader.GetInt32(1),
                ClienteId = reader.GetInt32(2),
                TipoEvento = reader.GetString(3),
                Detalhes = reader.IsDBNull(4) ? null : reader.GetString(4),
                OcorreuEmUtc = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                CorrelationId = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                StatusAnterior = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                StatusNovo = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                ErroCodigo = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                ExecutadaComAtraso = reader.GetInt32(10) != 0
            });
        }
        return result;
    }

    private static void Bind(SqliteCommand cmd, AncorarPdfConfiguracaoTarefa configuracao)
    {
        cmd.Parameters.AddWithValue("$tarefaId", configuracao.TarefaId);
        cmd.Parameters.AddWithValue("$clienteId", configuracao.ClienteId);
        cmd.Parameters.AddWithValue("$esteiraId", configuracao.EsteiraId);
        cmd.Parameters.AddWithValue("$nomeTarefaPersonalizado", configuracao.NomeTarefaPersonalizado);
        cmd.Parameters.AddWithValue("$pastaMonitoradaPath", configuracao.PastaMonitoradaPath);
        cmd.Parameters.AddWithValue("$pdfModeloPath", configuracao.PdfModeloPath);
        cmd.Parameters.AddWithValue("$nomeReferenciaArquivo", configuracao.NomeReferenciaArquivo);
        cmd.Parameters.AddWithValue("$monitorarSubpastas", configuracao.MonitorarSubpastas ? 1 : 0);
        cmd.Parameters.AddWithValue("$validacaoClienteAtiva", configuracao.ValidacaoClienteAtiva ? 1 : 0);
        cmd.Parameters.AddWithValue("$limiarSimilaridadeNome", configuracao.LimiarSimilaridadeNome);
        cmd.Parameters.AddWithValue("$highlightOpacity", configuracao.HighlightOpacity);
        cmd.Parameters.AddWithValue("$modoSelecao", configuracao.ModoSelecao.ToString());
        cmd.Parameters.AddWithValue("$pdfModeloCrossCliente", configuracao.PdfModeloCrossCliente ? 1 : 0);
        cmd.Parameters.AddWithValue("$pdfModeloCrossClienteJustificativa", (object?)configuracao.PdfModeloCrossClienteJustificativa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$recorrencia", configuracao.Recorrencia.ToString());
        cmd.Parameters.AddWithValue("$agendamentoSegundo", configuracao.AgendamentoSegundo);
        cmd.Parameters.AddWithValue("$timezoneId", configuracao.TimezoneId);
        cmd.Parameters.AddWithValue("$prioridadeExecucao", Math.Clamp(configuracao.PrioridadeExecucao, 1, 5));
        cmd.Parameters.AddWithValue("$dstHorarioInvalidoPolicy", configuracao.DstHorarioInvalidoPolicy.ToString());
        cmd.Parameters.AddWithValue("$dstHorarioAmbiguoPolicy", configuracao.DstHorarioAmbiguoPolicy.ToString());
        cmd.Parameters.AddWithValue("$templateAncorasJson", JsonSerializer.Serialize(configuracao.TemplateAncoras, SerializerOptions));
        cmd.Parameters.AddWithValue("$ocrFallbackAtivo", configuracao.OcrFallbackAtivo ? 1 : 0);
        cmd.Parameters.AddWithValue("$ocrDpi", Math.Clamp(configuracao.OcrDpi, 150, 600));
        cmd.Parameters.AddWithValue("$ocrLang", configuracao.OcrLang);
        cmd.Parameters.AddWithValue("$programadoPorUserId", configuracao.ProgramadoPorUserId);
        cmd.Parameters.AddWithValue("$programadoPorNome", configuracao.ProgramadoPorNome);
        cmd.Parameters.AddWithValue("$programadoEmUtc", configuracao.ProgramadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$atualizadoPorUserId", configuracao.AtualizadoPorUserId);
        cmd.Parameters.AddWithValue("$atualizadoEmUtc", configuracao.AtualizadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$versaoTemplate", configuracao.VersaoTemplate);
    }

    private AncorarPdfConfiguracaoTarefa Map(SqliteDataReader reader)
    {
        var anchorsJson = reader.GetString(reader.GetOrdinal("TemplateAncorasJson"));
        var anchors = JsonSerializer.Deserialize<IReadOnlyList<AncorarPdfTemplateAncora>>(anchorsJson, SerializerOptions) ?? [];

        return new AncorarPdfConfiguracaoTarefa
        {
            TarefaId = reader.GetInt32(reader.GetOrdinal("TarefaId")),
            ClienteId = reader.GetInt32(reader.GetOrdinal("ClienteId")),
            EsteiraId = reader.GetInt32(reader.GetOrdinal("EsteiraId")),
            NomeTarefaPersonalizado = reader.GetString(reader.GetOrdinal("NomeTarefaPersonalizado")),
            PastaMonitoradaPath = reader.GetString(reader.GetOrdinal("PastaMonitoradaPath")),
            PdfModeloPath = reader.GetString(reader.GetOrdinal("PdfModeloPath")),
            NomeReferenciaArquivo = reader.GetString(reader.GetOrdinal("NomeReferenciaArquivo")),
            MonitorarSubpastas = reader.GetInt32(reader.GetOrdinal("MonitorarSubpastas")) == 1,
            ValidacaoClienteAtiva = reader.GetInt32(reader.GetOrdinal("ValidacaoClienteAtiva")) == 1,
            LimiarSimilaridadeNome = reader.GetDouble(reader.GetOrdinal("LimiarSimilaridadeNome")),
            HighlightOpacity = reader.GetDouble(reader.GetOrdinal("HighlightOpacity")),
            ModoSelecao = ReadModoSelecao(reader, "ModoSelecao"),
            PdfModeloCrossCliente = reader.GetInt32(reader.GetOrdinal("PdfModeloCrossCliente")) == 1,
            PdfModeloCrossClienteJustificativa = ReadNullableString(reader, "PdfModeloCrossClienteJustificativa"),
            Recorrencia = ReadRecorrencia(reader, "Recorrencia"),
            AgendamentoSegundo = reader.GetInt32(reader.GetOrdinal("AgendamentoSegundo")),
            TimezoneId = ReadString(reader, "TimezoneId", "UTC"),
            PrioridadeExecucao = ReadInt(reader, "PrioridadeExecucao", 3),
            DstHorarioInvalidoPolicy = ReadDstInvalidoPolicy(reader, "DstHorarioInvalidoPolicy"),
            DstHorarioAmbiguoPolicy = ReadDstAmbiguoPolicy(reader, "DstHorarioAmbiguoPolicy"),
            TemplateAncoras = anchors,
            OcrFallbackAtivo = ReadBool(reader, "OcrFallbackAtivo", false),
            OcrDpi = ReadInt(reader, "OcrDpi", 300),
            OcrLang = ReadString(reader, "OcrLang", "por+eng"),
            ProgramadoPorUserId = reader.GetInt32(reader.GetOrdinal("ProgramadoPorUserId")),
            ProgramadoPorNome = reader.GetString(reader.GetOrdinal("ProgramadoPorNome")),
            ProgramadoEmUtc = ReadDate(reader, "ProgramadoEmUtc"),
            AtualizadoPorUserId = reader.GetInt32(reader.GetOrdinal("AtualizadoPorUserId")),
            AtualizadoEmUtc = ReadDate(reader, "AtualizadoEmUtc"),
            VersaoTemplate = reader.GetInt32(reader.GetOrdinal("VersaoTemplate"))
        };
    }

    private static TarefaRecorrencia ReadRecorrencia(SqliteDataReader reader, string column)
    {
        var raw = reader.GetString(reader.GetOrdinal(column));
        return Enum.TryParse<TarefaRecorrencia>(raw, true, out var recorrencia)
            ? recorrencia
            : TarefaRecorrencia.Nenhuma;
    }

    private DateTime ReadDate(SqliteDataReader reader, string column)
    {
        var raw = reader.GetString(reader.GetOrdinal(column));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(SqliteAncorarPdfConfiguracaoRepository)}.{column}: '{raw}'.");
    }

    private static AncorarPdfModoSelecao ReadModoSelecao(SqliteDataReader reader, string column)
    {
        var raw = reader.GetString(reader.GetOrdinal(column));
        return Enum.TryParse<AncorarPdfModoSelecao>(raw, true, out var modo)
            ? modo
            : AncorarPdfModoSelecao.RetanguloLivre;
    }

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string ReadString(SqliteDataReader reader, string column, string fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return fallback;

        var raw = reader.GetString(ordinal);
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw;
    }

    private static bool ReadBool(SqliteDataReader reader, string column, bool fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return fallback;
        return reader.GetInt32(ordinal) != 0;
    }

    private static int ReadInt(SqliteDataReader reader, string column, int fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return fallback;

        return reader.GetInt32(ordinal);
    }

    private static AncorarPdfDstHorarioInvalidoPolicy ReadDstInvalidoPolicy(SqliteDataReader reader, string column)
    {
        var raw = ReadString(reader, column, AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido.ToString());
        return Enum.TryParse<AncorarPdfDstHorarioInvalidoPolicy>(raw, true, out var policy)
            ? policy
            : AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido;
    }

    private static AncorarPdfDstHorarioAmbiguoPolicy ReadDstAmbiguoPolicy(SqliteDataReader reader, string column)
    {
        var raw = ReadString(reader, column, AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo.ToString());
        return Enum.TryParse<AncorarPdfDstHorarioAmbiguoPolicy>(raw, true, out var policy)
            ? policy
            : AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo;
    }

    private AncorarPdfBacklogPendenteItem MapBacklog(SqliteDataReader reader)
    {
        return new AncorarPdfBacklogPendenteItem
        {
            BacklogId = reader.GetInt64(reader.GetOrdinal("Id")),
            TarefaId = reader.GetInt32(reader.GetOrdinal("TarefaId")),
            ClienteId = reader.GetInt32(reader.GetOrdinal("ClienteId")),
            JanelaAlvoUtc = ReadDate(reader, "JanelaAlvoUtc"),
            AtrasoSegundos = reader.GetInt32(reader.GetOrdinal("AtrasoSegundos")),
            Motivo = ReadString(reader, "Motivo", "misfire_offline"),
            DetectadoEmUtc = ReadDate(reader, "DetectadoEmUtc"),
            Status = ReadBacklogStatus(reader, "Status")
        };
    }

    private static AncorarPdfBacklogStatus ReadBacklogStatus(SqliteDataReader reader, string column)
    {
        var raw = ReadString(reader, column, AncorarPdfBacklogStatus.Pendente.ToString());
        return Enum.TryParse<AncorarPdfBacklogStatus>(raw, true, out var status)
            ? status
            : AncorarPdfBacklogStatus.Pendente;
    }
}
