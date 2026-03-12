using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Tarefas.Telemetry;

namespace Protons.Infrastructure.Tarefas.Services;

internal sealed class AncorarPdfExecutionWorker : IDisposable
{
    private static readonly Meter _meter = new("Protons.AncorarPdfFila", "1.0.0");
    private static readonly Counter<long> _retryTotal = _meter.CreateCounter<long>(
        "ancorar_pdf.fila.retry_total",
        description: "Total de retentativas tecnicas executadas no worker.");
    private static readonly Histogram<double> _processLatencyMs = _meter.CreateHistogram<double>(
        "ancorar_pdf.fila.process_file_latency_ms",
        unit: "ms",
        description: "Latência de processamento por item no worker. SLO: p95<=1500ms p99<=3000ms.");
    private static readonly Counter<long> _timeoutTotal = _meter.CreateCounter<long>(
        "ancorar_pdf.fila.timeout_total",
        description: "Total de timeouts de processamento no worker.");
    private static readonly Counter<long> _failuresTotal = _meter.CreateCounter<long>(
        "ancorar_pdf.fila.failures_total",
        description: "Total de falhas no worker com tag de categoria.");
    private static readonly Counter<long> _circuitBreakerOpenedTotal = _meter.CreateCounter<long>(
        "ancorar_pdf.fila.circuit_breaker_opened_total",
        description: "Total de vezes que o circuit breaker abriu o circuito.");

    private readonly string _workerId;
    private readonly ChannelReader<AncorarPdfFilaItem> _channelReader;
    private readonly IAncorarPdfFilaExecucaoRepository _filaRepo;
    private readonly IAncorarPdfExecucaoLeaseRepository _leaseRepo;
    private readonly IAncorarPdfMotorExecucao _motor;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuracao;
    private readonly TimeSpan _processoTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly ResiliencePipeline _pipeline;
    private readonly Action<string, string?>? _onLog;

    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    public AncorarPdfExecutionWorker(
        string workerId,
        ChannelReader<AncorarPdfFilaItem> channelReader,
        IAncorarPdfFilaExecucaoRepository filaRepo,
        IAncorarPdfExecucaoLeaseRepository leaseRepo,
        IAncorarPdfMotorExecucao motor,
        TimeProvider timeProvider,
        TimeSpan leaseDuracao,
        TimeSpan processoTimeout,
        TimeSpan? retryDelay = null,
        Action<string, string?>? onLog = null)
    {
        _workerId = workerId;
        _channelReader = channelReader;
        _filaRepo = filaRepo;
        _leaseRepo = leaseRepo;
        _motor = motor;
        _timeProvider = timeProvider;
        _leaseDuracao = leaseDuracao;
        _processoTimeout = processoTimeout;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
        _onLog = onLog;

        _pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _processoTimeout,
                Name = "processo_timeout"
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
                Name = "circuit_breaker_motor",
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null
                    && args.Outcome.Exception is not AncorarPdfFalhaDeNegocioException
                    && args.Outcome.Exception is not OperationCanceledException),
                OnOpened = _ =>
                {
                    _circuitBreakerOpenedTotal.Add(1);
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = _retryDelay,
                Name = "retry_tecnico",
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null
                    && args.Outcome.Exception is not AncorarPdfFalhaDeNegocioException
                    && args.Outcome.Exception is not OperationCanceledException),
                OnRetry = _ =>
                {
                    _retryTotal.Add(1);
                    return default;
                }
            })
            .Build();
    }

    public void Start(CancellationToken hostCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
        _workerTask = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        _cts?.Cancel();
        if (_workerTask is not null)
        {
            try
            {
                await _workerTask.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AncorarPdfFilaItem item;
            try
            {
                item = await _channelReader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ProcessarItemAsync(item, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessarItemAsync(AncorarPdfFilaItem item, CancellationToken ct)
    {
        var parentCtx = AncorarPdfActivitySource.CreateParentContext(item.TraceId, item.SpanId);
        var activity = parentCtx is { } ctx
            ? AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.worker.process", ActivityKind.Internal, ctx)
            : AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.worker.process", ActivityKind.Internal);
        using (activity)
        {
            if (activity != null && activity.IsAllDataRequested)
            {
                activity.SetTag("ancorar_pdf.correlation_id", item.CorrelationId);
                activity.SetTag("ancorar_pdf.tarefa_id", item.TarefaId);
                activity.SetTag("ancorar_pdf.cliente_id", item.ClienteId);
                activity.SetTag("ancorar_pdf.fila_item_id", item.FilaItemId);
            }

            await ProcessarItemCoreAsync(item, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessarItemCoreAsync(AncorarPdfFilaItem item, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lease = _leaseRepo.TentarAcquirir(
            item.FilaItemId, item.TarefaId, item.ClienteId, _workerId, _leaseDuracao);

        if (lease is null)
        {
            _onLog?.Invoke("warn",
                $"fila_claim_perdido filaItemId={item.FilaItemId} workerId={_workerId}");
            return;
        }

        _filaRepo.AtualizarStatus(item.FilaItemId, AncorarPdfFilaStatus.EmProcessamento,
            iniciadoEmUtc: now);

        AncorarPdfFilaFalhaCategoria? categoriaFalha = null;
        string? erroCodigo = null;
        string? erroDetalhe = null;
        bool concluido = false;
        string resultadoTag = "falha_tecnica";
        string categoriaTagMetrica = "tecnica";

        var sw = Stopwatch.StartNew();
        try
        {
            await _pipeline.ExecuteAsync(async innerCt =>
            {
                var resultado = await _motor.ExecutarAsync(item, innerCt).ConfigureAwait(false);
                if (!resultado.Sucesso)
                {
                    if (resultado.Categoria == AncorarPdfFilaFalhaCategoria.Negocio)
                    {
                        if (resultado.Error is not null)
                            throw new AncorarPdfFalhaDeNegocioException(resultado.Error);
                        throw new AncorarPdfFalhaDeNegocioException(
                            resultado.ErroCodigo ?? "negocio",
                            resultado.ErroDetalhe ?? "falha de negócio");
                    }
                    throw new InvalidOperationException(
                        $"motor falhou: {resultado.ErroCodigo} — {resultado.ErroDetalhe}");
                }
            }, ct).ConfigureAwait(false);

            concluido = true;
            resultadoTag = "sucesso";
            categoriaTagMetrica = "nenhuma";
            var finNow = _timeProvider.GetUtcNow().UtcDateTime;
            _filaRepo.AtualizarStatus(item.FilaItemId, AncorarPdfFilaStatus.Concluido,
                finalizadoEmUtc: finNow);
            _leaseRepo.Liberar(lease.LeaseId, "concluido");
            _onLog?.Invoke("info",
                $"fila_concluido filaItemId={item.FilaItemId} tarefaId={item.TarefaId} duracaoMs={sw.ElapsedMilliseconds}");
        }
        catch (TimeoutRejectedException)
        {
            _timeoutTotal.Add(1);
            categoriaFalha = AncorarPdfFilaFalhaCategoria.Tecnica;
            erroCodigo = "timeout";
            erroDetalhe = $"excedeu {_processoTimeout.TotalSeconds}s";
            resultadoTag = "falha_tecnica";
            categoriaTagMetrica = "tecnica";
        }
        catch (BrokenCircuitException ex)
        {
            categoriaFalha = AncorarPdfFilaFalhaCategoria.Tecnica;
            erroCodigo = "circuit_breaker_open";
            erroDetalhe = ex.InnerException?.Message ?? "circuito aberto por falhas consecutivas";
            resultadoTag = "falha_tecnica";
            categoriaTagMetrica = "tecnica";
        }
        catch (AncorarPdfFalhaDeNegocioException ex)
        {
            categoriaFalha = AncorarPdfFilaFalhaCategoria.Negocio;
            erroCodigo = ex.Codigo;
            erroDetalhe = ex.Message;
            resultadoTag = "falha_negocio";
            categoriaTagMetrica = "negocio";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelamento solicitado — item NÃO entra em idempotência, estado = Cancelado.
            resultadoTag = "cancelado";
            categoriaTagMetrica = "nenhuma";
            _filaRepo.AtualizarStatus(item.FilaItemId, AncorarPdfFilaStatus.Cancelado,
                finalizadoEmUtc: _timeProvider.GetUtcNow().UtcDateTime);
            _leaseRepo.Liberar(lease.LeaseId, "cancelado_host");
            _onLog?.Invoke("info",
                $"fila_cancelado filaItemId={item.FilaItemId} canceladoPorNome=host");
            return;
        }
        catch (Exception ex)
        {
            categoriaFalha = AncorarPdfFilaFalhaCategoria.Tecnica;
            erroCodigo = "excecao_nao_tratada";
            erroDetalhe = ex.Message;
            resultadoTag = "falha_tecnica";
            categoriaTagMetrica = "tecnica";
        }
        finally
        {
            sw.Stop();
            _processLatencyMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("resultado", resultadoTag),
                new KeyValuePair<string, object?>("categoria", categoriaTagMetrica));
        }

        if (!concluido)
        {
            var tentativa = item.TentativaAtual + 1;
            var finNow = _timeProvider.GetUtcNow().UtcDateTime;
            var categoriaTag = categoriaFalha switch
            {
                AncorarPdfFilaFalhaCategoria.Tecnica => "tecnica",
                AncorarPdfFilaFalhaCategoria.Negocio => "negocio",
                _ => "desconhecida"
            };

            _failuresTotal.Add(1, new KeyValuePair<string, object?>("categoria", categoriaTag));
            _filaRepo.AtualizarStatus(item.FilaItemId, AncorarPdfFilaStatus.Falhou,
                finalizadoEmUtc: finNow,
                categoriaFalha: categoriaFalha,
                erroCodigo: erroCodigo,
                erroDetalhe: erroDetalhe,
                tentativaAtual: tentativa);
            _leaseRepo.Liberar(lease.LeaseId, erroCodigo ?? "falha");

            if (categoriaFalha == AncorarPdfFilaFalhaCategoria.Tecnica)
                _onLog?.Invoke("warn",
                    $"fila_falhou_tecnico filaItemId={item.FilaItemId} erroCodigo={erroCodigo} tentativa={tentativa}");
            else
                _onLog?.Invoke("warn",
                    $"fila_falhou_negocio filaItemId={item.FilaItemId} erroCodigo={erroCodigo}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
