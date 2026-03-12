using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Tarefas.Telemetry;

namespace Protons.Infrastructure.Tarefas.Services;

// Motor real de ancoragem PDF.
// Orquestra 6 estágios: descoberta → idempotência → extração → validação → ancoragem → persistência.
public sealed class AncorarPdfMotorExecucao : IAncorarPdfMotorExecucao
{
    // OTel metrics (System.Diagnostics.Metrics — built-in .NET 8).
    private static readonly Meter _meter = new("Protons.AncorarPdfMotor", "1.0.0");

    private static readonly Histogram<double> _extracaoMs =
        _meter.CreateHistogram<double>(
            "ancorar_pdf_motor_extracao_ms", unit: "ms",
            description: "Latência extração PdfPig por PDF. SLO: p95<=900ms p99<=1800ms.");

    private static readonly Histogram<double> _totalMs =
        _meter.CreateHistogram<double>(
            "ancorar_pdf_motor_total_ms", unit: "ms",
            description: "Latência total do motor por item de fila.");

    private static readonly Counter<long> _palavrasTotal =
        _meter.CreateCounter<long>(
            "ancorar_pdf_motor_palavras_total",
            description: "Total de palavras extraídas por PDF processado.");

    private static readonly Counter<long> _decisao =
        _meter.CreateCounter<long>(
            "ancorar_pdf_motor_decisao",
            description: "Contagem de decisões do motor por tipo.");

    // Dependências injetadas.
    private readonly IAncorarPdfConfiguracaoRepository _configuracaoRepo;
    private readonly IAncorarPdfExecucaoRepository _execucaoRepo;
    private readonly IAncorarPdfSaidaRepository _saidaRepo;
    private readonly IAncorarPdfSeletorArquivo _seletorArquivo;
    private readonly IAncorarPdfExtratorTexto _extratorTexto;
    private readonly IAncorarPdfValidadorCliente _validadorCliente;
    private readonly IAncorarPdfAncoradorEspacial _ancoradorEspacial;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string, string?>? _onLog;

    public AncorarPdfMotorExecucao(
        IAncorarPdfConfiguracaoRepository configuracaoRepo,
        IAncorarPdfExecucaoRepository execucaoRepo,
        IAncorarPdfSaidaRepository saidaRepo,
        IAncorarPdfSeletorArquivo seletorArquivo,
        IAncorarPdfExtratorTexto extratorTexto,
        IAncorarPdfValidadorCliente validadorCliente,
        IAncorarPdfAncoradorEspacial ancoradorEspacial,
        TimeProvider timeProvider,
        Action<string, string?>? onLog = null)
    {
        _configuracaoRepo = configuracaoRepo;
        _execucaoRepo = execucaoRepo;
        _saidaRepo = saidaRepo;
        _seletorArquivo = seletorArquivo;
        _extratorTexto = extratorTexto;
        _validadorCliente = validadorCliente;
        _ancoradorEspacial = ancoradorEspacial;
        _timeProvider = timeProvider;
        _onLog = onLog;
    }

    public async Task<AncorarPdfMotorResultado> ExecutarAsync(
        AncorarPdfFilaItem item, CancellationToken ct)
    {
        using var activity = AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.motor.execute", ActivityKind.Internal);
        if (activity != null && activity.IsAllDataRequested)
        {
            activity.SetTag("ancorar_pdf.correlation_id", item.CorrelationId);
            activity.SetTag("ancorar_pdf.tarefa_id", item.TarefaId);
            activity.SetTag("ancorar_pdf.cliente_id", item.ClienteId);
            activity.SetTag("ancorar_pdf.fila_item_id", item.FilaItemId);
        }

        var swTotal = Stopwatch.StartNew();
        var agora = _timeProvider.GetUtcNow().UtcDateTime;

        // E1 — Carregar configuração da tarefa.
        var config = _configuracaoRepo.ObterPorTarefaId(item.TarefaId);
        if (config is null)
        {
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "config_nao_encontrada"));
            EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoFalhou,
                AncorarPdfErroCodigos.NegConfigNaoEncontrada,
                AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusBloqueada,
                executadaComAtraso: false, agora,
                $"config_nao_encontrada tarefa_id={item.TarefaId}");
            return Negocio(item, AncorarPdfErroCodigos.NegConfigNaoEncontrada,
                $"Configuração não encontrada para TarefaId={item.TarefaId}");
        }

        // Evento: motor iniciou o processamento.
        EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoIniciada,
            AncorarPdfErroCodigos.Nenhum,
            AncorarPdfErroCodigos.StatusAgendada, AncorarPdfErroCodigos.StatusEmAndamento,
            executadaComAtraso: false, agora,
            $"ciclo={item.CicloId} arquivo_referencia={config.NomeReferenciaArquivo}");

        // E2 — Descobrir e selecionar arquivo PDF.
        SelecaoArquivoResultado? selecao;
        try
        {
            selecao = await _seletorArquivo.SelecionarMelhorAsync(
                config.PastaMonitoradaPath,
                config.NomeReferenciaArquivo,
                config.LimiarSimilaridadeNome,
                config.MonitorarSubpastas,
                item.CicloId,
                ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // IOException → Polly no worker retenta; outras exceções propagam.
            Log("warn", $"seletor_arquivo_erro item={item.FilaItemId}: {ex.Message}");
            throw;
        }

        if (selecao is null)
        {
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "arquivo_nao_encontrado"));
            EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoFalhou,
                AncorarPdfErroCodigos.NegArquivoNaoEncontrado,
                AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusBloqueada,
                executadaComAtraso: false, agora,
                $"pasta={config.PastaMonitoradaPath} referencia={config.NomeReferenciaArquivo}");
            return Negocio(item, AncorarPdfErroCodigos.NegArquivoNaoEncontrado,
                $"Nenhum PDF com similaridade≥{config.LimiarSimilaridadeNome} " +
                $"em '{config.PastaMonitoradaPath}' referência='{config.NomeReferenciaArquivo}'");
        }

        // E3 — Idempotência: reservar processamento deste arquivo neste ciclo.
        var reserva = _execucaoRepo.TentarReservarProcessamento(
            item.TarefaId,
            item.CicloId,
            selecao.NomeEsperadoLogico,
            selecao.ArquivoHash,
            selecao.ArquivoPath,
            selecao.TamanhoBytes,
            selecao.MtimeUtc);

        if (!reserva.Reservado)
        {
            Log("info",
                $"ciclo_ja_processado item={item.FilaItemId} " +
                $"ciclo={item.CicloId} arquivo={selecao.NomeEsperadoLogico}");
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "sucesso"));
            return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
        }

        // E4 — Extrair texto do PDF (PdfPig; fallback OCR se config.OcrFallbackAtivo).
        IReadOnlyList<PdfPaginaTexto> paginas;
        var swExtra = Stopwatch.StartNew();
        try
        {
            if (config.OcrFallbackAtivo)
            {
                Log("info",
                    $"ocr_fallback_attempt correlationId={item.CorrelationId} tarefaId={item.TarefaId} ocr_enabled=true ocr_lang={config.OcrLang} ocr_dpi={config.OcrDpi}");
            }

            AncorarPdfOcrConfigContext.Current = config;
            try
            {
                paginas = await _extratorTexto.ExtrairAsync(selecao.ArquivoPath, ct);
            }
            finally
            {
                AncorarPdfOcrConfigContext.Current = null;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (AncorarPdfFalhaDeNegocioException ex) when (
            string.Equals(ex.Codigo, AncorarPdfErroCodigos.NegOcrIndisponivel, StringComparison.Ordinal))
        {
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "ocr_indisponivel"));
            Log("warn",
                $"ocr_fallback_unavailable correlationId={item.CorrelationId} tarefaId={item.TarefaId} ocr_enabled={config.OcrFallbackAtivo} ocr_lang={config.OcrLang} ocr_dpi={config.OcrDpi} detalhe={ex.Message}");
            EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoFalhou,
                AncorarPdfErroCodigos.NegOcrIndisponivel,
                AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusBloqueada,
                executadaComAtraso: false, agora,
                $"arquivo={selecao.NomeEsperadoLogico} detalhe={ex.Message}");
            return Negocio(item, ex.Codigo, ex.Message);
        }
        catch (AncorarPdfFalhaDeNegocioException ex)
        {
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "negocio_extracao"));
            EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoFalhou,
                ex.Codigo,
                AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusBloqueada,
                executadaComAtraso: false, agora,
                $"arquivo={selecao.NomeEsperadoLogico} detalhe={ex.Message}");
            return Negocio(item, ex.Codigo, ex.Message);
        }
        catch (IOException)
        {
            // IO failure → Polly retenta.
            throw;
        }
        finally
        {
            swExtra.Stop();
            _extracaoMs.Record(swExtra.Elapsed.TotalMilliseconds);
        }

        var totalPalavras = paginas.Sum(p => p.Palavras.Count);
        _palavrasTotal.Add(totalPalavras);

        if (config.OcrFallbackAtivo)
        {
            Log("info",
                $"ocr_fallback_result correlationId={item.CorrelationId} tarefaId={item.TarefaId} ocr_enabled=true ocr_lang={config.OcrLang} ocr_dpi={config.OcrDpi} ocr_result={(totalPalavras > 0 ? "success" : "no_text")} palavras={totalPalavras}");
        }

        if (totalPalavras == 0)
        {
            if (config.OcrFallbackAtivo)
            {
                Log("warn",
                    $"ocr_fallback_no_text correlationId={item.CorrelationId} tarefaId={item.TarefaId} ocr_enabled=true ocr_lang={config.OcrLang} ocr_dpi={config.OcrDpi}");
            }

            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "pdf_sem_texto"));
            EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoFalhou,
                AncorarPdfErroCodigos.NegPdfSemTexto,
                AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusBloqueada,
                executadaComAtraso: false, agora,
                $"arquivo={selecao.NomeEsperadoLogico}");
            return Negocio(item, AncorarPdfErroCodigos.NegPdfSemTexto,
                config.OcrFallbackAtivo
                    ? $"PDF '{selecao.ArquivoPath}' sem texto nativo (escaneado ou vazio), OCR foi tentado sem sucesso."
                    : $"PDF '{selecao.ArquivoPath}' sem texto nativo (escaneado ou vazio).");
        }

        // E5 — Validação de cliente (opcional por configuração).
        if (config.ValidacaoClienteAtiva)
        {
            var validacao = _validadorCliente.Validar(paginas, item.ClienteId);
            if (!validacao.Valido)
            {
                _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "cliente_nao_validado"));
                EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoFalhou,
                    AncorarPdfErroCodigos.NegClienteNaoValidado,
                    AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusBloqueada,
                    executadaComAtraso: false, agora,
                    $"motivo={validacao.Motivo} arquivo={selecao.NomeEsperadoLogico}");
                return Negocio(item, validacao.Motivo ?? AncorarPdfErroCodigos.NegClienteNaoValidado,
                    $"Validação de cliente falhou: {validacao.Motivo}");
            }
        }

        // E6 — Ancorar variáveis nos bboxes do template.
        var variaveis = _ancoradorEspacial.Ancorar(paginas, config.TemplateAncoras);

        // E7 — Construir entidades de execução/saída.
        var agoraFinal = _timeProvider.GetUtcNow().UtcDateTime;
        var execucaoId = Guid.NewGuid().ToString();
        var saidaId = Guid.NewGuid().ToString();

        var execucao = new TarefaAncorarPdfExecucao
        {
            ExecucaoId = execucaoId,
            TarefaId = item.TarefaId,
            ClienteId = item.ClienteId,
            EsteiraId = config.EsteiraId,
            CicloId = item.CicloId,
            JanelaAlvoUtc = item.JanelaAlvoUtc,
            IniciadaEmUtc = agoraFinal,
            FinalizadaEmUtc = agoraFinal,
            Status = "Concluida",
            ProgramadoPorUserId = item.EnfileiradoPorUserId,
            ProgramadoPorNome = item.EnfileiradoPorNome,
            ProgramadoEmUtc = item.EnfileiradoEmUtc,
            CorrelationId = item.CorrelationId,
            CriadoEmUtc = agoraFinal
        };

        var resultados = variaveis.Select(v => new ResultadoAncoraVariavel
        {
            ResultadoId = Guid.NewGuid().ToString(),
            ExecucaoId = execucaoId,
            TarefaId = item.TarefaId,
            ClienteId = item.ClienteId,
            ArquivoPath = selecao.ArquivoPath,
            ArquivoHash = selecao.ArquivoHash,
            Chave = v.Chave,
            ValorBruto = v.ValorBruto,
            ValorNormalizado = v.ValorNormalizado,
            Tipo = v.Tipo,
            CorTemplate = v.CorTemplate,
            Confianca = v.Confianca,
            Pagina = v.Pagina,
            BboxRelativoJson = v.BboxRelativo is not null
                ? JsonSerializer.Serialize(v.BboxRelativo)
                : null
        }).ToList();

        var saida = new SaidaVariavelAncorada
        {
            SaidaId = saidaId,
            ExecucaoId = execucaoId,
            TarefaId = item.TarefaId,
            ClienteId = item.ClienteId,
            ArquivoPath = selecao.ArquivoPath,
            ArquivoHash = selecao.ArquivoHash,
            ArquivoNomeLogico = selecao.NomeEsperadoLogico,
            DataExecucaoUtc = agoraFinal,
            SchemaVersion = 1,
            Variaveis = variaveis,
            CriadoEmUtc = agoraFinal
        };

        // Calcula hash canônico V2 antes de verificar deduplicação.
        var payloadHash = AncorarPdfPayloadHash.Computar(saida);
        saida = saida with { PayloadHashSha256 = payloadHash };

        // E8 — Deduplicação por conteúdo: evita persistir saída idêntica duas vezes.
        if (_saidaRepo.ExisteSaidaComHash(item.TarefaId, payloadHash))
        {
            Log("info",
                $"saida_deduplicada item={item.FilaItemId} hash={payloadHash[..8]}…");
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "sucesso"));
            return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
        }

        // Compatibilidade: considera deduplicação com hash V1 legado já persistido.
        var payloadHashV1 = AncorarPdfPayloadHash.ComputarV1Legado(saida);
        if (!string.Equals(payloadHash, payloadHashV1, StringComparison.Ordinal)
            && _saidaRepo.ExisteSaidaComHash(item.TarefaId, payloadHashV1))
        {
            Log("info",
                $"saida_deduplicada_legado_v1 item={item.FilaItemId} hash={payloadHashV1[..8]}…");
            _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "sucesso"));
            return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
        }

        // E9 — Persistência atômica: execução + resultados + saída em uma transação.
        _execucaoRepo.SalvarExecucaoCompleta(new AncorarPdfSalvarExecucaoEntrada
        {
            Execucao = execucao,
            Resultados = resultados,
            Saida = saida
        });

        // E10 — Log, métricas finais e evento de conclusão.
        swTotal.Stop();
        _totalMs.Record(swTotal.Elapsed.TotalMilliseconds);
        _decisao.Add(1, new KeyValuePair<string, object?>("tipo", "sucesso"));

        EmitirEvento(item, AncorarPdfErroCodigos.ExecucaoConcluida,
            AncorarPdfErroCodigos.Nenhum,
            AncorarPdfErroCodigos.StatusEmAndamento, AncorarPdfErroCodigos.StatusConcluida,
            executadaComAtraso: false, agoraFinal,
            $"variaveis={variaveis.Count} palavras={totalPalavras} total_ms={swTotal.Elapsed.TotalMilliseconds:F0}");

        Log("info",
            $"motor_concluido item={item.FilaItemId} " +
            $"variaveis={variaveis.Count} " +
            $"palavras={totalPalavras} " +
            $"total_ms={swTotal.Elapsed.TotalMilliseconds:F0}");

        return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
    }

    private static AncorarPdfMotorResultado Negocio(AncorarPdfFilaItem item, string codigo, string detalhe)
    {
        var error = AncorarPdfErrorCatalog.Create(codigo, detalhe, item.CorrelationId);
        return new AncorarPdfMotorResultado(false, AncorarPdfFilaFalhaCategoria.Negocio, codigo, detalhe, error);
    }

    private void Log(string level, string msg)
        => _onLog?.Invoke(level, msg);

    /// <summary>
    /// Emite um evento operacional canônico C7 via IAncorarPdfConfiguracaoRepository.
    /// Fire-and-forget seguro: falha na emissão não deve parar o motor.
    /// </summary>
    private void EmitirEvento(
        AncorarPdfFilaItem item,
        string tipoEvento,
        string erroCodigo,
        string statusAnterior,
        string statusNovo,
        bool executadaComAtraso,
        DateTime ocorreuEmUtc,
        string? detalhes = null)
    {
        try
        {
            _configuracaoRepo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId = item.TarefaId,
                ClienteId = item.ClienteId,
                TipoEvento = tipoEvento,
                Detalhes = detalhes,
                OcorreuEmUtc = ocorreuEmUtc,
                CorrelationId = item.CorrelationId,
                StatusAnterior = statusAnterior,
                StatusNovo = statusNovo,
                ErroCodigo = erroCodigo,
                ExecutadaComAtraso = executadaComAtraso
            });
        }
        catch (Exception ex)
        {
            // Falha na emissão de evento não pode parar o motor — apenas loga.
            Log("warn", $"evento_emit_falhou tipo={tipoEvento} correlation={item.CorrelationId}: {ex.Message}");
        }
    }
}
