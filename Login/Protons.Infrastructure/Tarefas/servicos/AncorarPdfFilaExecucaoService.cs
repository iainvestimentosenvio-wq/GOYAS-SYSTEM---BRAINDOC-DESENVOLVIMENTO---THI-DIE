using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Tarefas.Telemetry;

namespace Protons.Infrastructure.Tarefas.Services;

public sealed class AncorarPdfFilaExecucaoService : IAncorarPdfFilaExecucaoService
{
    // ------------------------------------------------------------------
    // Métricas OTel (System.Diagnostics.Metrics — built-in .NET 8)
    // ------------------------------------------------------------------
    private static readonly Meter _meter = new("Protons.AncorarPdfFila", "1.0.0");

    private static readonly Histogram<double> _enqueueLatencyMs =
        _meter.CreateHistogram<double>(
            "ancorar_pdf.fila.enqueue_latency_ms",
            unit: "ms",
            description: "Latência de enfileiramento. SLO: p95<=20ms p99<=50ms.");

    private static readonly Counter<long> _itemsTotal =
        _meter.CreateCounter<long>(
            "ancorar_pdf.fila.items_total",
            description: "Total de itens por decisão (enfileirado, concluido, falhou, cancelado, duplicado).");

    private readonly IAncorarPdfFilaExecucaoRepository _filaRepo;
    private readonly IAncorarPdfExecucaoLeaseRepository _leaseRepo;
    private readonly IAncorarPdfMotorExecucao _motor;
    private readonly TimeProvider _timeProvider;
    private readonly int _channelCapacity;
    private readonly TimeSpan _leaseDuracao;
    private readonly TimeSpan _processoTimeout;
    private readonly TimeSpan? _retryDelay;
    private readonly Action<string, string?>? _onLog;

    private readonly Channel<AncorarPdfFilaItem> _channel;

    private readonly List<AncorarPdfExecutionWorker> _workers = new();
    private CancellationTokenSource? _cts;
    private Task? _recoveryTask;
    private bool _started;
    private bool _disposed;

    // Observable gauge: reporta profundidade atual do channel ao collector
    private readonly ObservableGauge<int> _queueDepthGauge;

    public AncorarPdfFilaExecucaoService(
        IAncorarPdfFilaExecucaoRepository filaRepo,
        IAncorarPdfExecucaoLeaseRepository leaseRepo,
        IAncorarPdfMotorExecucao motor,
        TimeProvider? timeProvider = null,
        int channelCapacity = 1000,
        TimeSpan? leaseDuracao = null,
        TimeSpan? processoTimeout = null,
        TimeSpan? retryDelay = null,
        Action<string, string?>? onLog = null)
    {
        _filaRepo = filaRepo;
        _leaseRepo = leaseRepo;
        _motor = motor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _channelCapacity = Math.Max(10, channelCapacity);
        _leaseDuracao = leaseDuracao ?? TimeSpan.FromMinutes(5);
        _processoTimeout = processoTimeout ?? TimeSpan.FromSeconds(30);
        _retryDelay = retryDelay;
        _onLog = onLog;

        _channel = Channel.CreateBounded<AncorarPdfFilaItem>(new BoundedChannelOptions(_channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _queueDepthGauge = _meter.CreateObservableGauge(
            "ancorar_pdf.fila.queue_depth",
            () => _channel.Reader.Count,
            description: "Profundidade atual do channel (itens aguardando dequeue).");
    }

    public void Start(int numeroDeworkers = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Rehydration: reinjectar itens Aguardando sem lease ativo
        var reidratados = _filaRepo.ListarParaReidratar(limite: _channelCapacity);
        int countReid = 0;
        foreach (var item in reidratados)
        {
            if (_channel.Writer.TryWrite(item))
                countReid++;
        }
        if (countReid > 0)
            _onLog?.Invoke("info", $"fila_reidratado_startup count={countReid}");

        // Iniciar workers
        var count = Math.Max(1, numeroDeworkers);
        for (int i = 0; i < count; i++)
        {
            var worker = new AncorarPdfExecutionWorker(
                workerId: $"{Environment.MachineName}-worker-{i}",
                channelReader: _channel.Reader,
                filaRepo: _filaRepo,
                leaseRepo: _leaseRepo,
                motor: _motor,
                timeProvider: _timeProvider,
                leaseDuracao: _leaseDuracao,
                processoTimeout: _processoTimeout,
                retryDelay: _retryDelay,
                onLog: _onLog);
            _workers.Add(worker);
            worker.Start(ct);
        }

        // Task de recovery de leases expirados
        _recoveryTask = Task.Run(() => RecoveryLoopAsync(ct), ct);
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        if (!_started) return;
        _cts?.Cancel();
        _channel.Writer.TryComplete();

        var stopTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var tasks = _workers.Select(w => w.StopAsync(stopTimeout)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (_recoveryTask is not null)
        {
            try { await _recoveryTask.WaitAsync(stopTimeout).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
    }

    public async Task<AncorarPdfFilaEnfileirarResultado> EnfileirarAsync(
        AncorarPdfFilaEnfileirarEntrada entrada, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.fila.enqueue", ActivityKind.Internal);
        if (activity != null && activity.IsAllDataRequested)
        {
            activity.SetTag("ancorar_pdf.correlation_id", entrada.CorrelationId);
            activity.SetTag("ancorar_pdf.tarefa_id", entrada.TarefaId);
            activity.SetTag("ancorar_pdf.cliente_id", entrada.ClienteId);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var filaItemId = Guid.NewGuid().ToString("N");
        var current = Activity.Current;
        var item = new AncorarPdfFilaItem
        {
            FilaItemId = filaItemId,
            TarefaId = entrada.TarefaId,
            ClienteId = entrada.ClienteId,
            CicloId = entrada.CicloId,
            JanelaAlvoUtc = entrada.JanelaAlvoUtc,
            PrioridadeExecucao = entrada.PrioridadeExecucao,
            Status = AncorarPdfFilaStatus.Aguardando,
            Motivo = entrada.Motivo,
            EnfileiradoPorUserId = entrada.EnfileiradoPorUserId,
            EnfileiradoPorNome = entrada.EnfileiradoPorNome,
            EnfileiradoEmUtc = now,
            TentativaAtual = 0,
            TentativasMaximas = 3,
            CorrelationId = entrada.CorrelationId,
            CriadoEmUtc = now,
            TraceId = current?.TraceId.ToString(),
            SpanId = current?.SpanId.ToString()
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resultado = _filaRepo.Enfileirar(item);
        sw.Stop();
        _enqueueLatencyMs.Record(sw.Elapsed.TotalMilliseconds);

        if (!resultado.Enfileirado)
        {
            _itemsTotal.Add(1, new KeyValuePair<string, object?>("decisao", "duplicado"));
            return resultado;
        }

        _itemsTotal.Add(1, new KeyValuePair<string, object?>("decisao", "enfileirado"));
        _onLog?.Invoke("info",
            $"fila_enfileirado filaItemId={filaItemId} tarefaId={entrada.TarefaId} clienteId={entrada.ClienteId} cicloId={entrada.CicloId}");

        await _channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
        return resultado;
    }

    public async Task<AncorarPdfFilaCancelarResultado> CancelarAsync(
        string filaItemId, int userId, string nome, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cancelado = _filaRepo.TentarCancelar(filaItemId, nome, now);
        if (!cancelado)
            return new AncorarPdfFilaCancelarResultado(false, "item_nao_aguardando");

        _itemsTotal.Add(1, new KeyValuePair<string, object?>("decisao", "cancelado"));
        _onLog?.Invoke("info", $"fila_cancelado filaItemId={filaItemId} canceladoPorNome={nome}");
        return await Task.FromResult(new AncorarPdfFilaCancelarResultado(true)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AncorarPdfFilaItem>> ListarAsync(
        AncorarPdfFilaFiltro filtro, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await Task.FromResult(_filaRepo.Listar(filtro)).ConfigureAwait(false);
    }

    public int ObterProfundidadeAtual() => _channel.Reader.Count;

    private async Task RecoveryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
                var agora = _timeProvider.GetUtcNow().UtcDateTime;
                var expirados = _leaseRepo.ListarExpirados(agora, limite: 50);
                foreach (var lease in expirados)
                {
                    // Liberar o lease expirado e reinijetar o item no channel
                    _leaseRepo.Liberar(lease.LeaseId, "expirado_recovery");
                    var items = _filaRepo.Listar(new AncorarPdfFilaFiltro(
                        TarefaId: lease.TarefaId,
                        Status: AncorarPdfFilaStatus.Aguardando,
                        Limite: 1));
                    foreach (var item in items)
                    {
                        if (_channel.Writer.TryWrite(item))
                        {
                            _onLog?.Invoke("warn",
                                $"fila_lease_expirado_recuperado leaseId={lease.LeaseId} filaItemId={lease.FilaItemId}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke("error", $"recovery_loop_error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        foreach (var w in _workers) w.Dispose();
    }
}
