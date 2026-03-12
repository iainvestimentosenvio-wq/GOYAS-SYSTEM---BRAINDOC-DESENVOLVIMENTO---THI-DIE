using System.Globalization;
using System.Text.Json;
using Npgsql;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Tarefas.Repositories;

public sealed class PostgresAncorarPdfConfiguracaoRepository : IAncorarPdfConfiguracaoRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly PostgresDb _db;
    private readonly TimeProvider _timeProvider;

    public PostgresAncorarPdfConfiguracaoRepository(PostgresDb db, TimeProvider? timeProvider = null)
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
FROM ancorarpdfconfiguracoestarefa
WHERE tarefaid = @tarefaId
LIMIT 1;";
        cmd.Parameters.AddWithValue("@tarefaId", tarefaId);

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
INSERT INTO ancorarpdfconfiguracoestarefa (
  tarefaid,
  clienteid,
  esteiraid,
  nometarefapersonalizado,
  pastamonitoradapath,
  pdfmodelopath,
  nomereferenciaarquivo,
  monitorarsubpastas,
  validacaoclienteativa,
  limiarsimilaridadenome,
  highlightopacity,
  modoselecao,
  pdfmodelocrosscliente,
  pdfmodelocrossclientejustificativa,
  recorrencia,
  agendamentosegundo,
  timezoneid,
  prioridadeexecucao,
  dsthorarioinvalidopolicy,
  dsthorarioambiguopolicy,
  templateancorasjson,
  ocrfallbackativo,
  ocrdpi,
  ocrlang,
  programadoporuserid,
  programadopornome,
  programadoemutc,
  atualizadoporuserid,
  atualizadoemutc,
  versaotemplate
)
VALUES (
  @tarefaId,
  @clienteId,
  @esteiraId,
  @nomeTarefaPersonalizado,
  @pastaMonitoradaPath,
  @pdfModeloPath,
  @nomeReferenciaArquivo,
  @monitorarSubpastas,
  @validacaoClienteAtiva,
  @limiarSimilaridadeNome,
  @highlightOpacity,
  @modoSelecao,
  @pdfModeloCrossCliente,
  @pdfModeloCrossClienteJustificativa,
  @recorrencia,
  @agendamentoSegundo,
  @timezoneId,
  @prioridadeExecucao,
  @dstHorarioInvalidoPolicy,
  @dstHorarioAmbiguoPolicy,
  @templateAncorasJson,
  @ocrFallbackAtivo,
  @ocrDpi,
  @ocrLang,
  @programadoPorUserId,
  @programadoPorNome,
  @programadoEmUtc,
  @atualizadoPorUserId,
  @atualizadoEmUtc,
  @versaoTemplate
)
ON CONFLICT(tarefaid) DO UPDATE SET
  clienteid = excluded.clienteid,
  esteiraid = excluded.esteiraid,
  nometarefapersonalizado = excluded.nometarefapersonalizado,
  pastamonitoradapath = excluded.pastamonitoradapath,
  pdfmodelopath = excluded.pdfmodelopath,
  nomereferenciaarquivo = excluded.nomereferenciaarquivo,
  monitorarsubpastas = excluded.monitorarsubpastas,
  validacaoclienteativa = excluded.validacaoclienteativa,
  limiarsimilaridadenome = excluded.limiarsimilaridadenome,
  highlightopacity = excluded.highlightopacity,
  modoselecao = excluded.modoselecao,
  pdfmodelocrosscliente = excluded.pdfmodelocrosscliente,
  pdfmodelocrossclientejustificativa = excluded.pdfmodelocrossclientejustificativa,
  recorrencia = excluded.recorrencia,
  agendamentosegundo = excluded.agendamentosegundo,
  timezoneid = excluded.timezoneid,
  prioridadeexecucao = excluded.prioridadeexecucao,
  dsthorarioinvalidopolicy = excluded.dsthorarioinvalidopolicy,
  dsthorarioambiguopolicy = excluded.dsthorarioambiguopolicy,
  templateancorasjson = excluded.templateancorasjson,
  ocrfallbackativo = excluded.ocrfallbackativo,
  ocrdpi = excluded.ocrdpi,
  ocrlang = excluded.ocrlang,
  programadoporuserid = excluded.programadoporuserid,
  programadopornome = excluded.programadopornome,
  programadoemutc = excluded.programadoemutc,
  atualizadoporuserid = excluded.atualizadoporuserid,
  atualizadoemutc = excluded.atualizadoemutc,
  versaotemplate = excluded.versaotemplate;
";

            Bind(cmd, configuracao);
            cmd.ExecuteNonQuery();
        }

        if (historicoAlteracao is not null)
        {
            using var hist = connection.CreateCommand();
            hist.Transaction = tx;
            hist.CommandText = @"
INSERT INTO ancorarpdftemplatehistorico (
  tarefaid,
  versao,
  antesjson,
  depoisjson,
  alteradoporuserid,
  alteradopornome,
  alteradoemutc
)
VALUES (
  @tarefaId,
  @versao,
  @antesJson,
  @depoisJson,
  @alteradoPorUserId,
  @alteradoPorNome,
  @alteradoEmUtc
);
";
            hist.Parameters.AddWithValue("@tarefaId", historicoAlteracao.TarefaId);
            hist.Parameters.AddWithValue("@versao", historicoAlteracao.Versao);
            hist.Parameters.AddWithValue("@antesJson", historicoAlteracao.AntesJson);
            hist.Parameters.AddWithValue("@depoisJson", historicoAlteracao.DepoisJson);
            hist.Parameters.AddWithValue("@alteradoPorUserId", historicoAlteracao.AlteradoPorUserId);
            hist.Parameters.AddWithValue("@alteradoPorNome", historicoAlteracao.AlteradoPorNome);
            hist.Parameters.AddWithValue("@alteradoEmUtc", historicoAlteracao.AlteradoEmUtc.ToString("o", CultureInfo.InvariantCulture));
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
FROM ancorarpdftemplatehistorico
WHERE tarefaid = @tarefaId
ORDER BY versao DESC
LIMIT @limite;";
        cmd.Parameters.AddWithValue("@tarefaId", tarefaId);
        cmd.Parameters.AddWithValue("@limite", limiteNormalizado);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            itens.Add(new AncorarPdfTemplateHistoricoItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                TarefaId = reader.GetInt32(reader.GetOrdinal("tarefaid")),
                Versao = reader.GetInt32(reader.GetOrdinal("versao")),
                AntesJson = reader.GetString(reader.GetOrdinal("antesjson")),
                DepoisJson = reader.GetString(reader.GetOrdinal("depoisjson")),
                AlteradoPorUserId = reader.GetInt32(reader.GetOrdinal("alteradoporuserid")),
                AlteradoPorNome = reader.GetString(reader.GetOrdinal("alteradopornome")),
                AlteradoEmUtc = ReadDate(reader, "alteradoemutc")
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
FROM ancorarpdfconfiguracoestarefa c
INNER JOIN tarefas t ON t.id = c.tarefaid
WHERE c.clienteid = @clienteId
  AND c.esteiraid = @esteiraId
  AND lower(c.nometarefapersonalizado) = lower(@nomeTarefa)
  AND t.ativa = TRUE
  AND (@tarefaIdIgnorar IS NULL OR c.tarefaid <> @tarefaIdIgnorar)
LIMIT 1;";
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        cmd.Parameters.AddWithValue("@esteiraId", esteiraId);
        cmd.Parameters.AddWithValue("@nomeTarefa", nomeTarefaPersonalizado.Trim());
        cmd.Parameters.AddWithValue("@tarefaIdIgnorar", (object?)tarefaIdIgnorar ?? DBNull.Value);

        return cmd.ExecuteScalar() is not null;
    }

    public IReadOnlyList<AncorarPdfSchedulerAgendamentoAtivo> ListarAgendamentosAtivosAte(DateTime referenciaUtc, int limite)
    {
        var itens = new List<AncorarPdfSchedulerAgendamentoAtivo>(Math.Clamp(limite, 1, 1000));

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT
  c.tarefaid,
  c.clienteid,
  t.vencimentoutc,
  t.criadoemutc,
  c.timezoneid,
  c.prioridadeexecucao,
  c.dsthorarioinvalidopolicy,
  c.dsthorarioambiguopolicy
FROM ancorarpdfconfiguracoestarefa c
INNER JOIN tarefas t ON t.id = c.tarefaid
WHERE t.ativa = TRUE
  AND t.status = @statusAgendada
  AND lower(t.ferramentaid) = @ferramentaId
  AND t.vencimentoutc <= @referenciaUtc
ORDER BY c.prioridadeexecucao ASC, t.criadoemutc ASC
LIMIT @limite;";
        cmd.Parameters.AddWithValue("@statusAgendada", TarefaStatus.Agendada.ToString());
        cmd.Parameters.AddWithValue("@ferramentaId", FerramentaTarefaIds.AncorarPdfCanonico);
        cmd.Parameters.AddWithValue("@referenciaUtc", referenciaUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@limite", Math.Clamp(limite, 1, 1000));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            itens.Add(new AncorarPdfSchedulerAgendamentoAtivo
            {
                TarefaId = reader.GetInt32(reader.GetOrdinal("tarefaid")),
                ClienteId = reader.GetInt32(reader.GetOrdinal("clienteid")),
                VencimentoUtc = ReadDate(reader, "vencimentoutc"),
                CriadoEmUtc = ReadDate(reader, "criadoemutc"),
                TimezoneId = ReadString(reader, "timezoneid", "UTC"),
                PrioridadeExecucao = ReadInt(reader, "prioridadeexecucao", 3),
                DstHorarioInvalidoPolicy = ReadDstInvalidoPolicy(reader, "dsthorarioinvalidopolicy"),
                DstHorarioAmbiguoPolicy = ReadDstAmbiguoPolicy(reader, "dsthorarioambiguopolicy")
            });
        }

        return itens;
    }

    public long RegistrarBacklogPendente(AncorarPdfSchedulerBacklogRegistro registro)
    {
        using var connection = _db.Open();
        using var tx = connection.BeginTransaction();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO ancorarpdfschedulerbacklogpendente (
  tarefaid,
  clienteid,
  janelaalvoutc,
  atrasosegundos,
  motivo,
  detectadoemutc,
  status
)
VALUES (
  @tarefaId,
  @clienteId,
  @janelaAlvoUtc,
  @atrasoSegundos,
  @motivo,
  @detectadoEmUtc,
  @status
)
ON CONFLICT (tarefaid, janelaalvoutc) DO NOTHING;";
            cmd.Parameters.AddWithValue("@tarefaId", registro.TarefaId);
            cmd.Parameters.AddWithValue("@clienteId", registro.ClienteId);
            cmd.Parameters.AddWithValue("@janelaAlvoUtc", registro.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@atrasoSegundos", Math.Max(0, registro.AtrasoSegundos));
            cmd.Parameters.AddWithValue("@motivo", string.IsNullOrWhiteSpace(registro.Motivo) ? "misfire_offline" : registro.Motivo.Trim());
            cmd.Parameters.AddWithValue("@detectadoEmUtc", registro.DetectadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@status", AncorarPdfBacklogStatus.Pendente.ToString());
            cmd.ExecuteNonQuery();
        }

        using var select = connection.CreateCommand();
        select.Transaction = tx;
        select.CommandText = @"
SELECT id
FROM ancorarpdfschedulerbacklogpendente
WHERE tarefaid = @tarefaId
  AND janelaalvoutc = @janelaAlvoUtc
LIMIT 1;";
        select.Parameters.AddWithValue("@tarefaId", registro.TarefaId);
        select.Parameters.AddWithValue("@janelaAlvoUtc", registro.JanelaAlvoUtc.ToString("o", CultureInfo.InvariantCulture));
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
FROM ancorarpdfschedulerbacklogpendente
WHERE id = @id
  AND status = @status
LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", backlogId);
        cmd.Parameters.AddWithValue("@status", AncorarPdfBacklogStatus.Pendente.ToString());

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
FROM ancorarpdfschedulerbacklogpendente
WHERE clienteid = @clienteId
  AND status = @status
ORDER BY detectadoemutc ASC, id ASC
LIMIT @limite;";
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        cmd.Parameters.AddWithValue("@status", AncorarPdfBacklogStatus.Pendente.ToString());
        cmd.Parameters.AddWithValue("@limite", Math.Clamp(limite, 1, 500));

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
UPDATE ancorarpdfschedulerbacklogpendente
SET status = @status,
    decididoemutc = @decididoEmUtc,
    decididoporuserid = @decididoPorUserId,
    decididopornome = @decididoPorNome,
    observacao = @observacao
WHERE id = @id
  AND status = @statusPendente;";
        cmd.Parameters.AddWithValue("@status", statusFinal.ToString());
        cmd.Parameters.AddWithValue("@decididoEmUtc", decididoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@decididoPorUserId", decididoPorUserId);
        cmd.Parameters.AddWithValue("@decididoPorNome", decididoPorNome.Trim());
        cmd.Parameters.AddWithValue("@observacao", (object?)observacao?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", backlogId);
        cmd.Parameters.AddWithValue("@statusPendente", AncorarPdfBacklogStatus.Pendente.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    public void RegistrarEventoScheduler(AncorarPdfSchedulerEventoRegistro eventoRegistro)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ancorarpdfschedulereventos (
  tarefaid, clienteid, tipoevento, detalhes, ocorreuemutc,
  correlationid, statusanterior, statusnovo, errocodigo, executadacomatraso
) VALUES (
  @tarefaId, @clienteId, @tipoEvento, @detalhes, @ocorreuEmUtc,
  @correlationId, @statusAnterior, @statusNovo, @erroCodigo, @executadaComAtraso
);";
        cmd.Parameters.AddWithValue("@tarefaId", eventoRegistro.TarefaId);
        cmd.Parameters.AddWithValue("@clienteId", eventoRegistro.ClienteId);
        cmd.Parameters.AddWithValue("@tipoEvento", eventoRegistro.TipoEvento.Trim());
        cmd.Parameters.AddWithValue("@detalhes", (object?)eventoRegistro.Detalhes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ocorreuEmUtc", eventoRegistro.OcorreuEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@correlationId", eventoRegistro.CorrelationId);
        cmd.Parameters.AddWithValue("@statusAnterior", eventoRegistro.StatusAnterior);
        cmd.Parameters.AddWithValue("@statusNovo", eventoRegistro.StatusNovo);
        cmd.Parameters.AddWithValue("@erroCodigo", eventoRegistro.ErroCodigo);
        cmd.Parameters.AddWithValue("@executadaComAtraso", eventoRegistro.ExecutadaComAtraso);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AncorarPdfEventoHistoricoItem> ListarEventosOperacionais(
        AncorarPdfEventoHistoricoFiltro filtro)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();

        var limite = Math.Clamp(filtro.Limite, 1, 1000);

        var sb = new System.Text.StringBuilder(@"
SELECT id, tarefaid, clienteid, tipoevento, detalhes, ocorreuemutc,
       correlationid, statusanterior, statusnovo, errocodigo, executadacomatraso
FROM ancorarpdfschedulereventos
WHERE clienteid = @clienteId");

        cmd.Parameters.AddWithValue("@clienteId", filtro.ClienteId);

        if (filtro.TarefaId.HasValue)
        {
            sb.Append(" AND tarefaid = @tarefaId");
            cmd.Parameters.AddWithValue("@tarefaId", filtro.TarefaId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filtro.TipoEvento))
        {
            sb.Append(" AND tipoevento = @tipoEvento");
            cmd.Parameters.AddWithValue("@tipoEvento", filtro.TipoEvento.Trim());
        }
        if (filtro.DataInicioUtc.HasValue)
        {
            sb.Append(" AND ocorreuemutc >= @dataInicio");
            cmd.Parameters.AddWithValue("@dataInicio", filtro.DataInicioUtc.Value);
        }
        if (filtro.DataFimUtc.HasValue)
        {
            sb.Append(" AND ocorreuemutc <= @dataFim");
            cmd.Parameters.AddWithValue("@dataFim", filtro.DataFimUtc.Value);
        }

        sb.Append(" ORDER BY ocorreuemutc DESC LIMIT @limite");
        cmd.Parameters.AddWithValue("@limite", limite);
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
                OcorreuEmUtc = reader.GetDateTime(5),
                CorrelationId = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                StatusAnterior = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                StatusNovo = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                ErroCodigo = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                ExecutadaComAtraso = reader.GetBoolean(10)
            });
        }
        return result;
    }

    private static void Bind(NpgsqlCommand cmd, AncorarPdfConfiguracaoTarefa configuracao)
    {
        cmd.Parameters.AddWithValue("@tarefaId", configuracao.TarefaId);
        cmd.Parameters.AddWithValue("@clienteId", configuracao.ClienteId);
        cmd.Parameters.AddWithValue("@esteiraId", configuracao.EsteiraId);
        cmd.Parameters.AddWithValue("@nomeTarefaPersonalizado", configuracao.NomeTarefaPersonalizado);
        cmd.Parameters.AddWithValue("@pastaMonitoradaPath", configuracao.PastaMonitoradaPath);
        cmd.Parameters.AddWithValue("@pdfModeloPath", configuracao.PdfModeloPath);
        cmd.Parameters.AddWithValue("@nomeReferenciaArquivo", configuracao.NomeReferenciaArquivo);
        cmd.Parameters.AddWithValue("@monitorarSubpastas", configuracao.MonitorarSubpastas);
        cmd.Parameters.AddWithValue("@validacaoClienteAtiva", configuracao.ValidacaoClienteAtiva);
        cmd.Parameters.AddWithValue("@limiarSimilaridadeNome", configuracao.LimiarSimilaridadeNome);
        cmd.Parameters.AddWithValue("@highlightOpacity", configuracao.HighlightOpacity);
        cmd.Parameters.AddWithValue("@modoSelecao", configuracao.ModoSelecao.ToString());
        cmd.Parameters.AddWithValue("@pdfModeloCrossCliente", configuracao.PdfModeloCrossCliente);
        cmd.Parameters.AddWithValue("@pdfModeloCrossClienteJustificativa", (object?)configuracao.PdfModeloCrossClienteJustificativa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recorrencia", configuracao.Recorrencia.ToString());
        cmd.Parameters.AddWithValue("@agendamentoSegundo", configuracao.AgendamentoSegundo);
        cmd.Parameters.AddWithValue("@timezoneId", configuracao.TimezoneId);
        cmd.Parameters.AddWithValue("@prioridadeExecucao", Math.Clamp(configuracao.PrioridadeExecucao, 1, 5));
        cmd.Parameters.AddWithValue("@dstHorarioInvalidoPolicy", configuracao.DstHorarioInvalidoPolicy.ToString());
        cmd.Parameters.AddWithValue("@dstHorarioAmbiguoPolicy", configuracao.DstHorarioAmbiguoPolicy.ToString());
        cmd.Parameters.AddWithValue("@templateAncorasJson", JsonSerializer.Serialize(configuracao.TemplateAncoras, SerializerOptions));
        cmd.Parameters.AddWithValue("@ocrFallbackAtivo", configuracao.OcrFallbackAtivo);
        cmd.Parameters.AddWithValue("@ocrDpi", Math.Clamp(configuracao.OcrDpi, 150, 600));
        cmd.Parameters.AddWithValue("@ocrLang", configuracao.OcrLang);
        cmd.Parameters.AddWithValue("@programadoPorUserId", configuracao.ProgramadoPorUserId);
        cmd.Parameters.AddWithValue("@programadoPorNome", configuracao.ProgramadoPorNome);
        cmd.Parameters.AddWithValue("@programadoEmUtc", configuracao.ProgramadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@atualizadoPorUserId", configuracao.AtualizadoPorUserId);
        cmd.Parameters.AddWithValue("@atualizadoEmUtc", configuracao.AtualizadoEmUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@versaoTemplate", configuracao.VersaoTemplate);
    }

    private AncorarPdfConfiguracaoTarefa Map(NpgsqlDataReader reader)
    {
        var anchorsJson = reader.GetString(reader.GetOrdinal("templateancorasjson"));
        var anchors = JsonSerializer.Deserialize<IReadOnlyList<AncorarPdfTemplateAncora>>(anchorsJson, SerializerOptions) ?? [];

        return new AncorarPdfConfiguracaoTarefa
        {
            TarefaId = reader.GetInt32(reader.GetOrdinal("tarefaid")),
            ClienteId = reader.GetInt32(reader.GetOrdinal("clienteid")),
            EsteiraId = reader.GetInt32(reader.GetOrdinal("esteiraid")),
            NomeTarefaPersonalizado = reader.GetString(reader.GetOrdinal("nometarefapersonalizado")),
            PastaMonitoradaPath = reader.GetString(reader.GetOrdinal("pastamonitoradapath")),
            PdfModeloPath = reader.GetString(reader.GetOrdinal("pdfmodelopath")),
            NomeReferenciaArquivo = reader.GetString(reader.GetOrdinal("nomereferenciaarquivo")),
            MonitorarSubpastas = reader.GetBoolean(reader.GetOrdinal("monitorarsubpastas")),
            ValidacaoClienteAtiva = reader.GetBoolean(reader.GetOrdinal("validacaoclienteativa")),
            LimiarSimilaridadeNome = reader.GetDouble(reader.GetOrdinal("limiarsimilaridadenome")),
            HighlightOpacity = reader.GetDouble(reader.GetOrdinal("highlightopacity")),
            ModoSelecao = ReadModoSelecao(reader, "modoselecao"),
            PdfModeloCrossCliente = reader.GetBoolean(reader.GetOrdinal("pdfmodelocrosscliente")),
            PdfModeloCrossClienteJustificativa = ReadNullableString(reader, "pdfmodelocrossclientejustificativa"),
            Recorrencia = ReadRecorrencia(reader, "recorrencia"),
            AgendamentoSegundo = reader.GetInt32(reader.GetOrdinal("agendamentosegundo")),
            TimezoneId = ReadString(reader, "timezoneid", "UTC"),
            PrioridadeExecucao = ReadInt(reader, "prioridadeexecucao", 3),
            DstHorarioInvalidoPolicy = ReadDstInvalidoPolicy(reader, "dsthorarioinvalidopolicy"),
            DstHorarioAmbiguoPolicy = ReadDstAmbiguoPolicy(reader, "dsthorarioambiguopolicy"),
            TemplateAncoras = anchors,
            OcrFallbackAtivo = ReadBool(reader, "ocrfallbackativo", false),
            OcrDpi = ReadInt(reader, "ocrdpi", 300),
            OcrLang = ReadString(reader, "ocrlang", "por+eng"),
            ProgramadoPorUserId = reader.GetInt32(reader.GetOrdinal("programadoporuserid")),
            ProgramadoPorNome = reader.GetString(reader.GetOrdinal("programadopornome")),
            ProgramadoEmUtc = ReadDate(reader, "programadoemutc"),
            AtualizadoPorUserId = reader.GetInt32(reader.GetOrdinal("atualizadoporuserid")),
            AtualizadoEmUtc = ReadDate(reader, "atualizadoemutc"),
            VersaoTemplate = reader.GetInt32(reader.GetOrdinal("versaotemplate"))
        };
    }

    private static TarefaRecorrencia ReadRecorrencia(NpgsqlDataReader reader, string column)
    {
        var raw = reader.GetString(reader.GetOrdinal(column));
        return Enum.TryParse<TarefaRecorrencia>(raw, true, out var recorrencia)
            ? recorrencia
            : TarefaRecorrencia.Nenhuma;
    }

    private DateTime ReadDate(NpgsqlDataReader reader, string column)
    {
        var raw = reader.GetString(reader.GetOrdinal(column));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();

        throw new FormatException($"Valor de data invalido em {nameof(PostgresAncorarPdfConfiguracaoRepository)}.{column}: '{raw}'.");
    }

    private static AncorarPdfModoSelecao ReadModoSelecao(NpgsqlDataReader reader, string column)
    {
        var raw = reader.GetString(reader.GetOrdinal(column));
        return Enum.TryParse<AncorarPdfModoSelecao>(raw, true, out var modo)
            ? modo
            : AncorarPdfModoSelecao.RetanguloLivre;
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string ReadString(NpgsqlDataReader reader, string column, string fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return fallback;

        var raw = reader.GetString(ordinal);
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw;
    }

    private static bool ReadBool(NpgsqlDataReader reader, string column, bool fallback)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? fallback : reader.GetBoolean(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return fallback;
        }
    }

    private static int ReadInt(NpgsqlDataReader reader, string column, int fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return fallback;

        return reader.GetInt32(ordinal);
    }

    private static AncorarPdfDstHorarioInvalidoPolicy ReadDstInvalidoPolicy(NpgsqlDataReader reader, string column)
    {
        var raw = ReadString(reader, column, AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido.ToString());
        return Enum.TryParse<AncorarPdfDstHorarioInvalidoPolicy>(raw, true, out var policy)
            ? policy
            : AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido;
    }

    private static AncorarPdfDstHorarioAmbiguoPolicy ReadDstAmbiguoPolicy(NpgsqlDataReader reader, string column)
    {
        var raw = ReadString(reader, column, AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo.ToString());
        return Enum.TryParse<AncorarPdfDstHorarioAmbiguoPolicy>(raw, true, out var policy)
            ? policy
            : AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo;
    }

    private AncorarPdfBacklogPendenteItem MapBacklog(NpgsqlDataReader reader)
    {
        return new AncorarPdfBacklogPendenteItem
        {
            BacklogId = reader.GetInt64(reader.GetOrdinal("id")),
            TarefaId = reader.GetInt32(reader.GetOrdinal("tarefaid")),
            ClienteId = reader.GetInt32(reader.GetOrdinal("clienteid")),
            JanelaAlvoUtc = ReadDate(reader, "janelaalvoutc"),
            AtrasoSegundos = reader.GetInt32(reader.GetOrdinal("atrasosegundos")),
            Motivo = ReadString(reader, "motivo", "misfire_offline"),
            DetectadoEmUtc = ReadDate(reader, "detectadoemutc"),
            Status = ReadBacklogStatus(reader, "status")
        };
    }

    private static AncorarPdfBacklogStatus ReadBacklogStatus(NpgsqlDataReader reader, string column)
    {
        var raw = ReadString(reader, column, AncorarPdfBacklogStatus.Pendente.ToString());
        return Enum.TryParse<AncorarPdfBacklogStatus>(raw, true, out var status)
            ? status
            : AncorarPdfBacklogStatus.Pendente;
    }
}
